using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Session;
using static KotobaSenpai.Core.PhraseAnalysis.SentenceBlockLayout;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>Validates and rebinds provider responses, maps current geometry and retains session group identities.</summary>
internal sealed class PhraseRunAssembler
{
    private readonly SessionEpoch _sessionEpoch;
    private readonly object _identityGate = new();
    private readonly Dictionary<(string SegmentText, string Language, bool Translation, string ModelGroupId), Guid> _sessionGroupIds = new();

    public PhraseRunAssembler(SessionEpoch sessionEpoch) => _sessionEpoch = sessionEpoch;

    public void ForgetSentence((string SegmentText, string Language, bool Translation) key)
    {
        lock (_identityGate)
            foreach (var groupKey in _sessionGroupIds.Keys
                .Where(groupKey => (groupKey.SegmentText, groupKey.Language, groupKey.Translation) == key).ToArray())
                _sessionGroupIds.Remove(groupKey);
    }

    public void ClearSessionIds()
    {
        lock (_identityGate)
        {
            _sessionGroupIds.Clear();
        }
    }

    public PhraseAnalysisRun BuildRun(IReadOnlyList<PhraseAnalysisRequest> requests,
        IReadOnlyList<PhraseAnalysisResult> results, long epoch)
    {
        // Evaluate each sentence independently: failed sentences are recorded as warnings and skipped, the rest proceed as usual, matching the "partially usable" positioning.
        var groups = new List<PhraseGroupView>();
        var wordMeanings = new List<WordMeaningView>();
        var sentences = new List<SentenceTranslation>();
        var warnings = new List<string>();
        var successfulSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        PhraseAnalysisOutcome? firstFailure = null;

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (!result.Succeeded)
            {
                var warning = result.Warning ?? DescribeFailure(result.Outcome);
                if (!warnings.Contains(warning))
                    warnings.Add(warning);
                firstFailure ??= result.Outcome;
                continue;
            }

            successfulSegmentIds.Add(requests[i].SegmentId);
            var request = requests[i];
            sentences.Add(new SentenceTranslation(request.SegmentId, request.SegmentText, request.ExplanationLanguage,
                request.IncludeSentenceTranslation, request.IncludeSentenceTranslation ? SentenceTranslation.Validate(result.Translation) : null));

            var tokenById = requests[i].Tokens.ToDictionary(reference => reference.Id);
            var validation = PhraseGroupValidator.ValidateAndBuild(result.Groups, tokenById);
            warnings.AddRange(validation.Warnings);
            groups.AddRange(validation.ValidGroups.Select(group => PhraseGeometryMapper.MapGroup(group.WithSessionId(
                GetSessionGroupId(request, group.ModelGroupId, epoch))) with { SegmentId = request.SegmentId }));

            var wordValidation = WordMeaningValidator.ValidateAndBuild(result.Words, requests[i].LocalSpans);
            warnings.AddRange(wordValidation.Warnings);
            wordMeanings.AddRange(wordValidation.ValidWords);
        }

        // As long as any (processable) sentence succeeds, the overall outcome is Success; only when all fail is the first failure category taken.
        var outcome = groups.Count > 0 || results.Any(result => result.Succeeded)
            ? PhraseAnalysisOutcome.Success
            : firstFailure ?? PhraseAnalysisOutcome.Success;

        return new PhraseAnalysisRun(
            outcome,
            groups.ToArray(),
            warnings.Count > 0 ? string.Join("; ", warnings) : null)
        {
            Words = wordMeanings.ToArray(),
            SuccessfulSegmentIds = successfulSegmentIds,
            Sentences = sentences.ToArray(),
        };
    }

    internal static PhraseAnalysisResult RebindResult(PhraseAnalysisRequest request,
        IReadOnlyList<SentenceTokenReference> tokens, PhraseAnalysisResult result)
    {
        if (tokens.Select(token => (token.Id, token.Token))
            .SequenceEqual(request.Tokens.Select(token => (token.Id, token.Token))))
            return result;

        // Quote recognition can shift every token id; bind unchanged content to the current OCR references.
        var content = GetContentTokens(tokens);
        var currentContent = GetContentTokens(request.Tokens);
        var ids = content.Select((token, index) => (token.Id, CurrentId: currentContent[index].Id))
            .ToDictionary(pair => pair.Id, pair => pair.CurrentId);
        var validGroupIds = PhraseGroupValidator.ValidateAndBuild(result.Groups, tokens.ToDictionary(token => token.Id))
            .ValidGroups.Select(group => group.ModelGroupId).ToHashSet(StringComparer.Ordinal);
        var groups = result.Groups
            // Removing a quote must not turn an originally invalid provider group into a valid group.
            .Where(group => validGroupIds.Contains(group.ModelGroupId))
            .DistinctBy(group => group.ModelGroupId, StringComparer.Ordinal)
            .Select(group => new ParsedPhraseGroup(group.ModelGroupId, group.Type,
                group.PartTokenIds.Select(part => (IReadOnlyList<SentenceTokenId>)part.Where(ids.ContainsKey).Select(id => ids[id]).ToArray())
                    .Where(part => part.Count > 0).ToArray(),
                group.Label, group.Meaning, group.Grammar))
            .Where(group => group.PartTokenIds.Count > 0)
            .ToArray();
        return new PhraseAnalysisResult(result.Outcome, groups, result.Warning)
        {
            Words = result.Words,
            Translation = result.Translation,
            Endpoint = result.Endpoint,
            ProviderStatusCode = result.ProviderStatusCode,
        };
    }

    private Guid GetSessionGroupId(PhraseAnalysisRequest request, string modelGroupId, long epoch)
    {
        lock (_identityGate)
        {
            // A late run can still be returned to its caller, but it cannot seed identities after reset.
            if (!_sessionEpoch.IsCurrent(epoch)) return Guid.NewGuid();
            var key = (NormalizeSegmentText(request.SegmentText), request.ExplanationLanguage, request.IncludeSentenceTranslation, modelGroupId);
            if (_sessionGroupIds.TryGetValue(key, out var id))
                return id;
            id = Guid.NewGuid();
            _sessionGroupIds[key] = id;
            return id;
        }
    }

    private static string DescribeFailure(PhraseAnalysisOutcome outcome) => outcome switch
    {
        PhraseAnalysisOutcome.NoKey => "Phrase analysis requires provider configuration.",
        PhraseAnalysisOutcome.Timeout => "Phrase analysis provider timed out.",
        PhraseAnalysisOutcome.Refused => "Phrase analysis provider refused the request.",
        PhraseAnalysisOutcome.MalformedJson => "Phrase analysis provider returned malformed JSON.",
        PhraseAnalysisOutcome.TransportError => "Phrase analysis provider transport error.",
        PhraseAnalysisOutcome.InvalidResponse => "Phrase analysis provider returned an invalid response.",
        _ => "Phrase analysis failed.",
    };

}
