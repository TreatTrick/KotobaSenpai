using System.Globalization;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class SettingsPageViewModelTests
{
    [Theory]
    [InlineData("en", null, "English")]
    [InlineData("zh-CN", null, "Simplified Chinese")]
    [InlineData("zh-CN", "French", "French")]
    public void Explanation_language_and_localized_status_follow_independent_preferences(
        string uiCulture, string? customLanguage, string expected)
    {
        var settings = new RecordingSettings();
        using var viewModel = Create(settings, new RecordingThemeStore(), uiCulture);
        var preferences = Assert.IsAssignableFrom<IExplanationLanguagePreferences>(viewModel.Language);
        viewModel.ExplanationLanguage = customLanguage ?? string.Empty;
        viewModel.SaveExplanationLanguageCommand.Execute(null);

        Assert.Equal(expected, preferences.EffectiveLanguage);
        Assert.Equal(uiCulture, viewModel.Language.CurrentCulture.Name);

        viewModel.Language.CurrentCulture = new CultureInfo("en");

        Assert.Equal(customLanguage ?? "English", preferences.EffectiveLanguage);
        Assert.Contains("Currently active:", viewModel.EffectiveExplanationLanguage);
        Assert.Contains("next recognition", viewModel.ExplanationLanguageStatus);
        Assert.Null(settings.GetValue("ApiKey"));
    }

    [Fact]
    public void Explanation_language_draft_only_applies_after_a_valid_save()
    {
        var settings = new RecordingSettings();
        using var viewModel = Create(settings, new RecordingThemeStore());
        viewModel.ExplanationLanguage = " French ";
        Assert.Null(settings.GetValue("ExplanationLanguage"));

        viewModel.SaveExplanationLanguageCommand.Execute(null);

        Assert.Equal("French", viewModel.ExplanationLanguage);
        Assert.Equal("French", settings.GetValue("ExplanationLanguage"));
        Assert.Contains("French", viewModel.EffectiveExplanationLanguage);
        Assert.Contains("next recognition", viewModel.ExplanationLanguageStatus);

        viewModel.ExplanationLanguage = "French\nEnglish";
        viewModel.SaveExplanationLanguageCommand.Execute(null);
        Assert.Equal("French", settings.GetValue("ExplanationLanguage"));
        Assert.DoesNotContain("next recognition", viewModel.ExplanationLanguageStatus);
    }

    [Theory]
    [InlineData(null, 1.0 / 3.0)]
    [InlineData("invalid", 1.0 / 3.0)]
    [InlineData("NaN", 1.0 / 3.0)]
    [InlineData("Infinity", 1.0 / 3.0)]
    [InlineData("-1", 1.0 / 3.0)]
    [InlineData("0.4", 0.4)]
    [InlineData("0.01", 0.1)]
    [InlineData("0.9", 0.5)]
    public void Restores_preferences_without_writing_them_back(string? rawScale, double expectedScale)
    {
        var settings = new RecordingSettings();
        settings.Values[FuriganaFontSettings.FontScaleKey] = rawScale;
        var theme = new RecordingThemeStore { Mode = AppThemeMode.Dark };
        using var viewModel = Create(settings, theme);

        Assert.Equal(expectedScale, viewModel.FuriganaFontScale);
        Assert.Equal(AppThemeMode.Dark, viewModel.SelectedTheme);
        Assert.Equal(0, settings.WriteCount);
        Assert.Equal(0, theme.SaveCount);
    }

    [Fact]
    public void Missing_theme_defaults_to_auto()
    {
        using var viewModel = Create(new RecordingSettings(), new RecordingThemeStore());

        Assert.Equal(AppThemeMode.Auto, viewModel.SelectedTheme);
    }

    [Fact]
    public void Theme_changes_persist_once_and_notify_the_view()
    {
        var theme = new RecordingThemeStore();
        using var viewModel = Create(new RecordingSettings(), theme);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.SelectedTheme = AppThemeMode.Light;
        viewModel.SelectedTheme = AppThemeMode.Light;

        Assert.Equal(AppThemeMode.Light, theme.Mode);
        Assert.Equal(1, theme.SaveCount);
        Assert.Equal(nameof(SettingsPageViewModel.SelectedTheme), Assert.Single(changes));
    }

    [Theory]
    [InlineData(0.42, "0.42")]
    [InlineData(0.01, "0.1")]
    [InlineData(0.9, "0.5")]
    public void Font_scale_changes_use_invariant_storage_and_the_slider_range(double requested, string expected)
    {
        var settings = new RecordingSettings();
        using var viewModel = Create(settings, new RecordingThemeStore());
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            viewModel.FuriganaFontScale = requested;

            Assert.Equal(expected, settings.GetValue(FuriganaFontSettings.FontScaleKey));
            Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), viewModel.FuriganaFontScale);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Language_changes_keep_theme_choice_identity_and_saved_preferences()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("en"));
        var languageStore = new RecordingLanguageStore();
        var language = new LanguageService(localizer, languageStore, () => new CultureInfo("en"), _ => { });
        language.Initialize();
        var settings = new RecordingSettings();
        var theme = new RecordingThemeStore { Mode = AppThemeMode.Dark };
        using var llm = new LlmSettingsViewModel(settings, new EmptyCatalog(), localizer);
        using var viewModel = new SettingsPageViewModel(settings, theme, language, llm, localizer, language);
        var choice = viewModel.ThemeModes.Single(option => option.Mode == AppThemeMode.Dark);
        var originalName = choice.Name;

        viewModel.Language.CurrentCulture = viewModel.Language.AvailableCultures.Single(culture => culture.Name == "zh-CN");

        Assert.Equal("zh-CN", languageStore.Saved);
        Assert.Same(choice, viewModel.ThemeModes.Single(option => option.Mode == AppThemeMode.Dark));
        Assert.NotEqual(originalName, choice.Name);
        Assert.Equal(AppThemeMode.Dark, viewModel.SelectedTheme);
        Assert.Equal(0, theme.SaveCount);
        Assert.Equal(0, settings.WriteCount);
    }

    [Fact]
    public void Disposal_releases_page_and_llm_localization_subscriptions()
    {
        var localizer = new RecordingLocalizer();
        var settings = new RecordingSettings();
        var llm = new LlmSettingsViewModel(settings, new EmptyCatalog(), localizer);
        var language = new LanguageService(LocalizerFactory.Create(new CultureInfo("en")),
            new RecordingLanguageStore(), () => new CultureInfo("en"), _ => { });
        var viewModel = new SettingsPageViewModel(settings, new RecordingThemeStore(), language, llm, localizer, language);
        Assert.Equal(2, localizer.SubscriberCount);

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Equal(0, localizer.SubscriberCount);
    }

    private static SettingsPageViewModel Create(RecordingSettings settings, RecordingThemeStore theme, string culture = "en")
    {
        var localizer = LocalizerFactory.Create(new CultureInfo(culture));
        var language = new LanguageService(localizer, new RecordingLanguageStore(), () => new CultureInfo(culture), _ => { }, settings);
        language.Initialize();
        return new SettingsPageViewModel(settings, theme, language,
            new LlmSettingsViewModel(settings, new EmptyCatalog(), localizer), localizer, language);
    }

    private sealed class RecordingSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        public Dictionary<string, string?> Values { get; } = [];
        public int WriteCount { get; private set; }
        public override string? GetValue(string key) => Values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) { Values[key] = value; WriteCount++; }
    }

    private sealed class RecordingThemeStore : IThemePreferenceStore
    {
        public AppThemeMode? Mode { get; set; }
        public int SaveCount { get; private set; }
        public AppThemeMode? Load() => Mode;
        public void Save(AppThemeMode mode) { Mode = mode; SaveCount++; }
    }

    private sealed class RecordingLanguageStore : ILanguagePreferenceStore
    {
        public string? Saved { get; private set; }
        public string? Load() => null;
        public void Save(string cultureName) => Saved = cultureName;
    }

    private sealed class EmptyCatalog : ILlmModelCatalog
    {
        public Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(LlmModelCatalogResult.Success([]));
    }

    private sealed class RecordingLocalizer : IStringLocalizer
    {
        private EventHandler? _cultureChanged;
        public int SubscriberCount => _cultureChanged?.GetInvocationList().Length ?? 0;
        public string Get(string key, params object[] args) => key;
        public event EventHandler? CultureChanged
        {
            add => _cultureChanged += value;
            remove => _cultureChanged -= value;
        }
    }
}
