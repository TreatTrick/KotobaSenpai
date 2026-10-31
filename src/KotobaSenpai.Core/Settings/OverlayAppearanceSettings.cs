using System.Globalization;

namespace KotobaSenpai.Core.Settings;

public enum OverlayOutlineColorMode { Auto, Black, White }

/// <summary>An immutable, platform-independent set of annotation and popup appearance preferences.</summary>
public sealed record OverlayAppearance(
    string FuriganaColor = "#00BFFF",
    string PitchHighColor = "#FF4444",
    string PitchLowColor = "#4488FF",
    string UnderlineColor = "#00BFFF",
    bool OutlineEnabled = true,
    OverlayOutlineColorMode OutlineColorMode = OverlayOutlineColorMode.Auto,
    double OutlineThickness = 1,
    double PopupBackgroundTransparency = 0.5);

public static class OverlayAppearanceSettings
{
    public const string FuriganaColorKey = "OverlayFuriganaColor";
    public const string PitchHighColorKey = "OverlayPitchHighColor";
    public const string PitchLowColorKey = "OverlayPitchLowColor";
    public const string UnderlineColorKey = "OverlayUnderlineColor";
    public const string OutlineEnabledKey = "OverlayOutlineEnabled";
    public const string OutlineColorModeKey = "OverlayOutlineColorMode";
    public const string OutlineThicknessKey = "OverlayOutlineThickness";
    public const string PopupBackgroundTransparencyKey = "PhrasePopupBackgroundTransparency";
    public const double MinimumOutlineThickness = 0.5;
    public const double MaximumOutlineThickness = 3;
    public static OverlayAppearance Default { get; } = new();
    public static IReadOnlyList<string> ColorKeys { get; } =
        [FuriganaColorKey, PitchHighColorKey, PitchLowColorKey, UnderlineColorKey];
    public static IReadOnlyList<string> Keys { get; } =
        [.. ColorKeys, OutlineEnabledKey, OutlineColorModeKey, OutlineThicknessKey, PopupBackgroundTransparencyKey];

    public static OverlayAppearance Load(ISettingsService settings)
    {
        var values = settings.GetSnapshot(Keys);
        return new(
            Resolve(values[FuriganaColorKey], Default.FuriganaColor),
            Resolve(values[PitchHighColorKey], Default.PitchHighColor),
            Resolve(values[PitchLowColorKey], Default.PitchLowColor),
            Resolve(values[UnderlineColorKey], Default.UnderlineColor),
            !bool.TryParse(values[OutlineEnabledKey], out var enabled) || enabled,
            values[OutlineColorModeKey]?.Trim().ToLowerInvariant() switch
            {
                "black" => OverlayOutlineColorMode.Black,
                "white" => OverlayOutlineColorMode.White,
                _ => OverlayOutlineColorMode.Auto,
            },
            double.TryParse(values[OutlineThicknessKey], NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness)
                && double.IsFinite(thickness) && thickness >= MinimumOutlineThickness && thickness <= MaximumOutlineThickness
                ? thickness : Default.OutlineThickness,
            double.TryParse(values[PopupBackgroundTransparencyKey], NumberStyles.Float, CultureInfo.InvariantCulture, out var transparency)
                && double.IsFinite(transparency) && transparency >= 0 && transparency <= 1
                ? transparency : Default.PopupBackgroundTransparency);
    }

    public static IReadOnlyDictionary<string, string?> Colors(OverlayAppearance appearance) => new Dictionary<string, string?>
    {
        [FuriganaColorKey] = appearance.FuriganaColor,
        [PitchHighColorKey] = appearance.PitchHighColor,
        [PitchLowColorKey] = appearance.PitchLowColor,
        [UnderlineColorKey] = appearance.UnderlineColor,
    };

    public static bool TryNormalizeColor(string? raw, out string color)
    {
        color = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        return color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
    }

    private static string Resolve(string? raw, string fallback)
        => TryNormalizeColor(raw, out var color) ? color : fallback;

    /// <summary>Uses the explicit color, or the higher contrast ratio from relative sRGB luminance; ties use black.</summary>
    public static string GetOutlineColor(string color, OverlayOutlineColorMode mode = OverlayOutlineColorMode.Auto)
    {
        if (mode == OverlayOutlineColorMode.Black)
            return "#000000";
        if (mode == OverlayOutlineColorMode.White)
            return "#FFFFFF";
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        var rgb = Convert.ToInt32(color[1..], 16);
        var luminance = 0.2126 * Linear((byte)(rgb >> 16))
            + 0.7152 * Linear((byte)(rgb >> 8)) + 0.0722 * Linear((byte)rgb);
        return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? "#000000" : "#FFFFFF";
    }
}
