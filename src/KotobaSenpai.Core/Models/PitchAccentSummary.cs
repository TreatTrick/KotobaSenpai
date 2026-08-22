using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.Models;

/// <summary>Local pitch data for one source token within a merged word or phrase part.</summary>
public sealed record PitchAccentSummary(
    string Surface,
    string Reading,
    int SurfaceOffset,
    int? AccentPosition,
    string? Notation)
{
    public static PitchAccentSummary FromToken(Token token, int surfaceOffset)
    {
        ArgumentNullException.ThrowIfNull(token);
        var pattern = PitchAccent.CreatePattern(token.Reading, token.PitchAccentPosition);
        return new(token.Surface, token.Reading, surfaceOffset, token.PitchAccentPosition, pattern?.Notation);
    }
}
