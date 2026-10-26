using System.Globalization;
using KotobaSenpai.App.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class ExplanationLanguageTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("zh-CN", "Simplified Chinese")]
    public void Missing_or_invalid_preference_follows_the_ui(string culture, string expected)
    {
        var settings = new MemorySettings();
        settings.SetValue("ExplanationLanguage", "Invalid\nName");
        var language = Create(settings, culture);

        Assert.Null(language.CustomLanguage);
        Assert.Equal(expected, language.EffectiveLanguage);
    }

    [Fact]
    public void Saved_name_is_trimmed_restored_and_independent_of_ui_language()
    {
        var settings = new MemorySettings();
        settings.SetValue("Model", "example-model");
        var language = Create(settings);
        var changes = 0;
        language.EffectiveLanguageChanged += (_, _) => changes++;

        Assert.True(language.TrySave(" French "));
        language.ChangeCulture(new CultureInfo("zh-CN"));
        Assert.True(language.TrySave("French"));

        Assert.Equal("French", language.EffectiveLanguage);
        Assert.Equal("French", Create(settings).CustomLanguage);
        Assert.Equal("example-model", settings.GetValue("Model"));
        Assert.Equal(1, changes);

        Assert.True(language.TrySave(" "));
        Assert.Null(settings.GetValue("ExplanationLanguage"));
        Assert.Equal("Simplified Chinese", language.EffectiveLanguage);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("French\nEnglish")]
    [InlineData("French\u2028English")]
    [InlineData("French\tEnglish")]
    [InlineData("French\0")]
    public void Invalid_input_keeps_the_saved_value(string invalid)
    {
        var settings = new MemorySettings();
        var language = Create(settings);
        Assert.True(language.TrySave("French"));

        Assert.False(language.TrySave(invalid));
        Assert.False(language.TrySave(new string('x', 81)));
        Assert.Equal("French", settings.GetValue("ExplanationLanguage"));
        Assert.Equal("French", language.EffectiveLanguage);
        Assert.True(language.TrySave(new string('x', 80)));
    }

    [Fact]
    public void Following_ui_notifies_only_when_the_effective_name_changes()
    {
        var language = Create(new MemorySettings());
        var changes = 0;
        language.EffectiveLanguageChanged += (_, _) => changes++;

        Assert.True(language.TrySave("English"));
        Assert.True(language.TrySave(null));
        Assert.Equal(0, changes);

        language.ChangeCulture(new CultureInfo("zh-CN"));
        Assert.Equal("Simplified Chinese", language.EffectiveLanguage);
        Assert.Equal(1, changes);
    }

    private static LanguageService Create(MemorySettings settings, string culture = "en")
    {
        var language = new LanguageService(LocalizerFactory.Create(new CultureInfo(culture)),
            new LocalAppDataLanguagePreferenceStore(settings), () => new CultureInfo(culture), _ => { }, settings);
        language.Initialize();
        return language;
    }

    private sealed class MemorySettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }
}
