using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Core.Tests.Overlay;

public sealed class OverlayAppearanceSettingsTests
{
    [Theory]
    [InlineData(null, 0.5)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("0.75", 0.75)]
    [InlineData("invalid", 0.5)]
    [InlineData("NaN", 0.5)]
    [InlineData("Infinity", 0.5)]
    [InlineData("-0.1", 0.5)]
    [InlineData("1.1", 0.5)]
    [InlineData("0,75", 0.5)]
    public void Popup_transparency_restores_valid_values_and_defaults_invalid_ones(string? raw, double expected)
    {
        var settings = new MemorySettings();
        settings.SetValue("PhrasePopupBackgroundTransparency", raw);

        Assert.Equal(expected, OverlayAppearanceSettings.Load(settings).PopupBackgroundTransparency);
    }

    [Theory]
    [InlineData(null, null, OverlayOutlineColorMode.Auto, 1)]
    [InlineData("white", "2.25", OverlayOutlineColorMode.White, 2.25)]
    [InlineData("BLACK", "0.5", OverlayOutlineColorMode.Black, 0.5)]
    [InlineData("auto", "3", OverlayOutlineColorMode.Auto, 3)]
    [InlineData("red", "NaN", OverlayOutlineColorMode.Auto, 1)]
    [InlineData("invalid", "4", OverlayOutlineColorMode.Auto, 1)]
    [InlineData(null, "0", OverlayOutlineColorMode.Auto, 1)]
    [InlineData(null, "1,5", OverlayOutlineColorMode.Auto, 1)]
    public void Outline_preferences_restore_valid_values_and_default_invalid_ones(
        string? mode, string? thickness, OverlayOutlineColorMode expectedMode, double expectedThickness)
    {
        var settings = new MemorySettings();
        settings.SetValue("OverlayOutlineColorMode", mode);
        settings.SetValue("OverlayOutlineThickness", thickness);
        var appearance = OverlayAppearanceSettings.Load(settings);
        Assert.Equal(expectedMode, appearance.OutlineColorMode);
        Assert.Equal(expectedThickness, appearance.OutlineThickness);
    }

    [Theory]
    [InlineData(OverlayOutlineColorMode.Black, "#000000")]
    [InlineData(OverlayOutlineColorMode.White, "#FFFFFF")]
    public void Manual_outline_mode_overrides_contrast_selection(OverlayOutlineColorMode mode, string expected)
    {
        Assert.Equal(expected, OverlayAppearanceSettings.GetOutlineColor("#000000", mode));
        Assert.Equal(expected, OverlayAppearanceSettings.GetOutlineColor("#FFFFFF", mode));
    }

    [Fact]
    public void Missing_and_invalid_values_fall_back_independently()
    {
        var settings = new MemorySettings();
        settings.SetValue("OverlayPitchHighColor", "#abcdef");
        settings.SetValue("OverlayPitchLowColor", "#80123456");
        settings.SetValue("OverlayOutlineEnabled", "invalid");

        var appearance = OverlayAppearanceSettings.Load(settings);

        Assert.Equal("#ABCDEF", appearance.PitchHighColor);
        Assert.Equal("#4488FF", appearance.PitchLowColor);
        Assert.Equal("#00BFFF", appearance.FuriganaColor);
        Assert.Equal("#00BFFF", appearance.UnderlineColor);
        Assert.True(appearance.OutlineEnabled);
        settings.SetValue("OverlayOutlineEnabled", "false");
        Assert.False(OverlayAppearanceSettings.Load(settings).OutlineEnabled);
    }

    [Theory]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData(" #123456 ", "#123456")]
    [InlineData(null, null)]
    [InlineData("Red", null)]
    [InlineData("#123", null)]
    [InlineData("#80123456", null)]
    [InlineData("#GG0000", null)]
    public void Only_opaque_hex_colors_are_accepted(string? raw, string? expected)
    {
        Assert.Equal(expected is not null, OverlayAppearanceSettings.TryNormalizeColor(raw, out var color));
        if (expected is not null)
            Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#0000FF", "#FFFFFF")]
    [InlineData("#FFFF00", "#000000")]
    public void Outline_chooses_the_stronger_black_or_white_contrast(string fill, string expected)
        => Assert.Equal(expected, OverlayAppearanceSettings.GetOutlineColor(fill));

    private sealed class MemorySettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }
}
