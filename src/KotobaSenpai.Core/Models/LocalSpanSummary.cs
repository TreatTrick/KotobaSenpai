namespace KotobaSenpai.Core.Models;

/// <summary>Summary of a locally resolved contiguous JMdict span, letting the LLM see the contiguous words already covered locally and avoid re-discovering them.</summary>
public sealed record LocalSpanSummary
{
    public LocalSpanSummary(
        string surface,
        string reading,
        string lookupKey,
        IReadOnlyList<SentenceTokenId> tokenIds,
        IReadOnlyList<PitchAccentSummary>? pitchAccents = null)
    {
        Surface = surface;
        Reading = reading;
        LookupKey = lookupKey;
        TokenIds = (tokenIds ?? throw new ArgumentNullException(nameof(tokenIds))).ToArray();
        PitchAccents = (pitchAccents ?? Array.Empty<PitchAccentSummary>()).ToArray();
    }

    public string Surface { get; }
    public string Reading { get; }
    public string LookupKey { get; }
    public IReadOnlyList<SentenceTokenId> TokenIds { get; }
    public IReadOnlyList<PitchAccentSummary> PitchAccents { get; }
}
