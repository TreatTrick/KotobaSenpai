using System.Globalization;
using System.Text;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.Dictionary;

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
        ArgumentNullException.ThrowIfNull(tokens);

        var forms = new HashSet<string>(StringComparer.Ordinal);
        var segments = SplitSegments(tokens);
        foreach (var segment in segments)
            CollectCandidateForms(segment, forms);

        var matches = forms.Count == 0
            ? new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal)
            : _lookup.LookupForms(forms);

        var result = new List<LookupSpan>();
        foreach (var segment in segments)
            ResolveSegment(segment, matches, result);
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
            foreach (var form in EnumerateCompoundForms(segment, start))
                forms.Add(form.LookupKey);
        }
    }

    private static IEnumerable<CompoundForm> EnumerateCompoundForms(IReadOnlyList<Token> segment, int start)
    {
        var surface = segment[start].Surface;
        var hasKanji = JapaneseText.ContainsKanji(surface);
        for (int end = start + 1; end < segment.Count; end++)
        {
            var token = segment[end];
            var prefix = surface;
            surface += token.Surface;
            if (surface.Length > MaxCandidateCharacters)
                yield break;

            yield return new CompoundForm(surface, end, surface.Length, IsInflected: false);

            var prefixHasKanji = hasKanji;
            hasKanji |= JapaneseText.ContainsKanji(token.Surface);
            if (!hasKanji || token.PartsOfSpeech.Pos1 is not ("動詞" or "形容詞"))
                continue;

            // Prefer the base spelling before trying the lexical lemma.
            foreach (var baseForm in new[] { token.OrthBase, token.Lemma }.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(baseForm) || baseForm == token.Surface
                    || prefix.Length + baseForm.Length > MaxCandidateCharacters
                    || (!prefixHasKanji && !JapaneseText.ContainsKanji(baseForm)))
                    continue;

                yield return new CompoundForm(prefix + baseForm, end, surface.Length, IsInflected: true);
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
            var reading = best is { IsCompound: true }
                ? GetCompoundReading(source, entries, best.InflectingTokenIndex - start)
                : null;
            output.Add(new LookupSpan(source, lookupKey, entries, reading));
            start = end;
        }
    }

    private static Candidate? FindBestCandidate(
        IReadOnlyList<Token> segment,
        int start,
        IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> matches)
    {
        Candidate? best = null;

        foreach (var form in EnumerateCompoundForms(segment, start))
        {
            if (!TryGetEntries(matches, form.LookupKey, out var entries))
                continue;

            var lastTokenIndex = form.LastTokenIndex;
            var end = lastTokenIndex + 1;
            if (form.IsInflected)
            {
                var prefixLength = form.SurfaceLength - segment[lastTokenIndex].Surface.Length;
                end = FindInflectionEnd(segment, lastTokenIndex, prefixLength);
            }

            var last = segment[end - 1];
            var surfaceLength = last.StartOffset + last.Surface.Length - segment[start].StartOffset;
            var candidate = new Candidate(end, form.LookupKey, entries, Priority: form.IsInflected ? 2 : 1,
                surfaceLength, IsCompound: true, InflectingTokenIndex: form.IsInflected ? lastTokenIndex : null);
            if (IsBetter(candidate, best))
                best = candidate;
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

    private static int FindInflectionEnd(IReadOnlyList<Token> segment, int start, int prefixLength = 0)
    {
        if (!IsInflectingBase(segment[start]))
            return start + 1;

        int end = start + 1;
        int characters = prefixLength + segment[start].Surface.Length;
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

    private static string? GetCompoundReading(
        IReadOnlyList<Token> tokens,
        IReadOnlyList<DictionaryEntry> entries,
        int? inflectingTokenIndex)
    {
        if (!tokens.Any(token => JapaneseText.ContainsKanji(token.Surface)))
            return null;

        var readings = entries.Select(entry => entry.Reading)
            .Where(reading => !string.IsNullOrWhiteSpace(reading))
            .Select(Kana.ToHiragana)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (readings.Length != 1 || !JapaneseText.IsHiraganaReading(readings[0]))
            return null;

        var reading = readings[0];
        if (inflectingTokenIndex is not int baseIndex)
            return reading;

        var baseReading = Kana.ToHiragana(tokens[baseIndex].BaseReading);
        if (!JapaneseText.IsHiraganaReading(baseReading) || reading.Length <= baseReading.Length
            || !reading.EndsWith(baseReading, StringComparison.Ordinal))
            return null;

        var occurringReadings = tokens.Skip(baseIndex).Select(token => Kana.ToHiragana(token.Reading)).ToArray();
        if (!occurringReadings.All(JapaneseText.IsHiraganaReading))
            return null;

        return reading[..^baseReading.Length] + string.Concat(occurringReadings);
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

    private readonly record struct CompoundForm(string LookupKey, int LastTokenIndex, int SurfaceLength, bool IsInflected);

    private sealed record Candidate(
        int EndExclusive,
        string LookupKey,
        IReadOnlyList<DictionaryEntry> Entries,
        int Priority,
        int SurfaceLength,
        bool IsCompound = false,
        int? InflectingTokenIndex = null);

    private sealed record TokenMatch(
        string LookupKey,
        IReadOnlyList<DictionaryEntry> Entries);
}
