using System.Globalization;

namespace KotobaSenpai.Core.Settings;

/// <summary>Shared storage contract and fallback for the furigana font scale.</summary>
public static class FuriganaFontSettings
{
    public const string FontScaleKey = "FuriganaFontScale";
    public const double DefaultFontScale = 1.0 / 3.0;

    /// <summary>Resolves a finite positive font scale, falling back to one third.</summary>
    public static double ResolveFontScale(string? raw)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            && double.IsFinite(scale) && scale > 0 ? scale : DefaultFontScale;
}
