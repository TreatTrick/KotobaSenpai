using System.Globalization;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Performs dictionary longest-match on complete UniDic token boundaries and solidify the matched results into
/// non-overlapping spans.
/// </summary>
public sealed class TokenBoundarySpanResolver : ITokenSpanResolver
{
    private const int MaxCandidateCharacters = 32;

    private readonly IBatchDictionaryLookup _lookup;

    public TokenBoundarySpanResolver(IBatchDictionaryLookup lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens)
    {
        var lines = ResolveMany(new[] { (IReadOnlyList<Token>)tokens });
        return lines.Count == 0 ? Array.Empty<LookupSpan>() : lines[0];
    }

    public IReadOnlyList<IReadOnlyList<LookupSpan>> ResolveMany(
        IReadOnlyList<IReadOnlyList<Token>> tokenLines)
    {
        ArgumentNullException.ThrowIfNull(tokenLines);
        if (tokenLines.Count == 0)
            return Array.Empty<IReadOnlyList<LookupSpan>>();

        var lineSegments = new List<IReadOnlyList<IReadOnlyList<Token>>>(tokenLines.Count);
        var forms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tokens in tokenLines)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            var segments = SplitSegments(tokens);
            lineSegments.Add(segments);
            foreach (var segment in segments)
                CollectCandidateForms(segment, forms);
        }

        var matches = forms.Count == 0
            ? new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal)
            : _lookup.LookupForms(forms);

        var result = new List<IReadOnlyList<LookupSpan>>(lineSegments.Count);
        foreach (var segments in lineSegments)
        {
            var lineResult = new List<LookupSpan>();
            foreach (var segment in segments)
                ResolveSegment(segment, matches, lineResult);
            result.Add(lineResult);
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<Token>> SplitSegments(IReadOnlyList<Token> tokens)
    {
        var segments = new List<IReadOnlyList<Token>>();
        var current = new List<Token>();

        foreach (var token in tokens)
        {
            if (IsBoundaryToken(token))
            {
                AddSegment();
                continue;
            }

            if (current.Count > 0 && !IsContiguous(current[^1], token))
                AddSegment();
            current.Add(token);
        }
        AddSegment();
        return segments;

        void AddSegment()
        {
            if (current.Count == 0)
                return;
            segments.Add(current.ToArray());
            current.Clear();
        }
    }

    private static void CollectCandidateForms(
        IReadOnlyList<Token> segment,
        ISet<string> forms)
    {
        foreach (var token in segment)
        {
            foreach (var key in TokenLookupKeys(token))
                forms.Add(key);
            if (!string.IsNullOrEmpty(token.Surface))
                forms.Add(token.Surface);
        }

        for (int start = 0; start < segment.Count; start++)
        {
            var surface = string.Empty;
            for (int end = start; end < segment.Count; end++)
            {
                surface += segment[end].Surface;
                if (surface.Length > MaxCandidateCharacters)
                    break;
                if (end > start)
                    forms.Add(surface);
            }
        }
    }

    private static void ResolveSegment(
        IReadOnlyList<Token> segment,
        IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> matches,
        ICollection<LookupSpan> output)
    {
        int start = 0;
        while (start < segment.Count)
        {
            var best = FindBestCandidate(segment, start, matches);
            var end = best?.EndExclusive ?? start + 1;
            var source = segment.Skip(start).Take(end - start).ToArray();
            var lookupKey = best?.LookupKey ?? source[0].Lemma;
            var entries = best?.Entries ?? Array.Empty<DictionaryEntry>();
            output.Add(new LookupSpan(source, lookupKey, entries));
            start = end;
        }
    }

    private static Candidate? FindBestCandidate(
        IReadOnlyList<Token> segment,
        int start,
        IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> matches)
    {
        Candidate? best = null;

        // Direct-surface candidates: only allowed to end at a complete token boundary.
        var surface = string.Empty;
        for (int end = start; end < segment.Count; end++)
        {
            surface += segment[end].Surface;
            if (surface.Length > MaxCandidateCharacters)
                break;
            if (end == start)
                continue;

            if (TryGetEntries(matches, surface, out var entries))
            {
                var candidate = new Candidate(end + 1, surface, entries, Priority: 1, surface.Length);
                if (IsBetter(candidate, best))
                    best = candidate;
            }
        }

        // A single-token lemma hit is the common basis for both inflection extension and the single-token fallback.
        var tokenMatch = FindTokenMatch(segment[start], matches);
        if (tokenMatch is not null)
        {
            var inflectionEnd = FindInflectionEnd(segment, start);
            if (inflectionEnd > start + 1)
            {
                var inflectedSurface = string.Concat(
                    segment.Skip(start).Take(inflectionEnd - start).Select(token => token.Surface));
                var candidate = new Candidate(
                    inflectionEnd,
                    tokenMatch.LookupKey,
                    tokenMatch.Entries,
                    Priority: 0,
                    inflectedSurface.Length);
                if (IsBetter(candidate, best))
                    best = candidate;
            }

            var single = new Candidate(
                start + 1,
                tokenMatch.LookupKey,
                tokenMatch.Entries,
                Priority: 2,
                segment[start].Surface.Length);
            if (IsBetter(single, best))
                best = single;
        }
        else if (TryGetEntries(matches, segment[start].Surface, out var surfaceEntries))
        {
            var singleSurface = new Candidate(
                start + 1,
                segment[start].Surface,
                surfaceEntries,
                Priority: 2,
                segment[start].Surface.Length);
            if (IsBetter(singleSurface, best))
                best = singleSurface;
        }

        // Keep the token's underline even when there is no dictionary result; empty entries are recognized by the caller as "resolved but not matched".
        if (best is null)
        {
            var token = segment[start];
            best = new Candidate(
                start + 1,
                string.IsNullOrEmpty(token.Lemma) ? token.Surface : token.Lemma,
                Array.Empty<DictionaryEntry>(),
                Priority: 3,
                token.Surface.Length);
        }

        return best;
    }

    private static TokenMatch? FindTokenMatch(
        Token token,
        IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> matches)
    {
        foreach (var key in TokenLookupKeys(token))
        {
            if (TryGetEntries(matches, key, out var entries))
                return new TokenMatch(key, entries);
        }
        return null;
    }

    private static int FindInflectionEnd(IReadOnlyList<Token> segment, int start)
    {
        if (!IsInflectingBase(segment[start]))
            return start + 1;

        int end = start + 1;
        int characters = segment[start].Surface.Length;
        while (end < segment.Count
            && characters + segment[end].Surface.Length <= MaxCandidateCharacters
            && IsContiguous(segment[end - 1], segment[end])
            && IsInflectionContinuation(segment[end]))
        {
            characters += segment[end].Surface.Length;
            end++;
        }
        return end;
    }

    private static IEnumerable<string> TokenLookupKeys(Token token)
    {
        var keys = new[]
        {
            token.Lemma,
            token.OrthBase,
            Kana.ToHiragana(token.Reading),
            Kana.ToHiragana(token.BaseReading),
        };
        return keys.Where(key => !string.IsNullOrEmpty(key)).Distinct(StringComparer.Ordinal);
    }

    private static bool TryGetEntries(
        IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> matches,
        string key,
        out IReadOnlyList<DictionaryEntry> entries)
    {
        if (matches.TryGetValue(key, out entries!) && entries.Count > 0)
            return true;
        entries = Array.Empty<DictionaryEntry>();
        return false;
    }

    private static bool IsBetter(Candidate candidate, Candidate? current)
        => current is null
            || candidate.SurfaceLength > current.SurfaceLength
            || (candidate.SurfaceLength == current.SurfaceLength
                && candidate.Priority < current.Priority);

    private static bool IsInflectingBase(Token token)
        => token.PartsOfSpeech.Pos1 is "動詞" or "形容詞" or "助動詞";

    private static bool IsInflectionContinuation(Token token)
        => token.PartsOfSpeech.Pos1 == "助動詞"
            || (token.PartsOfSpeech.Pos1 == "助詞"
                && token.PartsOfSpeech.Pos2 == "接続助詞"
                && token.Surface is "て" or "で");

    private static bool IsContiguous(Token previous, Token next)
        => previous.StartOffset + previous.Surface.Length == next.StartOffset;

    private static bool IsBoundaryToken(Token token)
        => token.Surface.Length > 0
            && token.Surface.All(c => char.IsWhiteSpace(c) || IsPunctuation(c));

    private static bool IsPunctuation(char c)
        => char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };

    private sealed record Candidate(
        int EndExclusive,
        string LookupKey,
        IReadOnlyList<DictionaryEntry> Entries,
        int Priority,
        int SurfaceLength);

    private sealed record TokenMatch(
        string LookupKey,
        IReadOnlyList<DictionaryEntry> Entries);
}
