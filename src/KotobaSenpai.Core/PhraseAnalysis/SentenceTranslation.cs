namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>A sentence result associated with the current local request, independently of word surfaces or geometry.</summary>
public sealed record SentenceTranslation(string SegmentId, string SourceText, string Language, bool Requested, string? Translation)
{
    public const int MaximumLength = 4000;

    public static string? Validate(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= MaximumLength ? trimmed : null;
    }
}
