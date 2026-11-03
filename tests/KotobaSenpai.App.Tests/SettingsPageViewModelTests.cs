using System.Globalization;
using System.IO;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class SettingsPageViewModelTests
{
    [Theory]
    [InlineData(null, LogLevel.Error)]
    [InlineData("invalid", LogLevel.Error)]
    [InlineData("999", LogLevel.Error)]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Critical", LogLevel.Critical)]
    public void Log_level_restores_saved_values_without_writing(string? raw, LogLevel expected)
    {
        var settings = new RecordingSettings();
        settings.Values["MinimumLogLevel"] = raw;
        using var viewModel = Create(settings, new RecordingThemeStore());

        Assert.Equal(expected, viewModel.SelectedLogLevel);
        Assert.Equal(Enum.GetValues<LogLevel>(), viewModel.LogLevels);
        Assert.Equal(0, settings.WriteCount);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Critical)]
    public void Log_level_changes_persist_once_and_restore(LogLevel level)
    {
        var settings = new RecordingSettings();
        settings.Values["Theme"] = "Dark";
        using var viewModel = Create(settings, new RecordingThemeStore());
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.SelectedLogLevel = level;
        viewModel.SelectedLogLevel = level;

        Assert.Equal(level.ToString(), settings.GetValue("MinimumLogLevel"));
        Assert.Equal("Dark", settings.GetValue("Theme"));
        Assert.Equal(1, settings.WriteCount);
        Assert.Equal(1, changes.Count(name => name == nameof(SettingsPageViewModel.SelectedLogLevel)));
        using var restored = Create(settings, new RecordingThemeStore());
        Assert.Equal(level, restored.SelectedLogLevel);
    }

    [Fact]
    public void Failed_log_level_save_preserves_the_committed_level_and_can_be_retried()
    {
        var settings = new RecordingSettings { FailWrites = true };
        using var viewModel = Create(settings, new RecordingThemeStore());

        viewModel.SelectedLogLevel = LogLevel.Trace;

        Assert.Equal(LogLevel.Error, viewModel.SelectedLogLevel);
        Assert.Null(settings.GetValue("MinimumLogLevel"));
        Assert.NotEmpty(viewModel.LogLevelError);

        settings.FailWrites = false;
        viewModel.SelectedLogLevel = LogLevel.Trace;

        Assert.Equal(LogLevel.Trace, viewModel.SelectedLogLevel);
        Assert.Empty(viewModel.LogLevelError);
    }

    [Fact]
    public void Log_level_changes_immediately_update_the_running_logger()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kslog_settings_" + Guid.NewGuid().ToString("N"));
        var settings = new RecordingSettings();
        using var viewModel = Create(settings, new RecordingThemeStore());
        try
        {
            using (var logger = new FileLogger(directory, () => LogConfiguration.LoadMinimumLevel(settings)))
            {
                logger.Log(LogLevel.Debug, null, "DEBUG_BEFORE");
                viewModel.SelectedLogLevel = LogLevel.Debug;
                logger.Log(LogLevel.Trace, null, "TRACE_BELOW_MINIMUM");
                logger.Log(LogLevel.Debug, null, "DEBUG_ENABLED");
                viewModel.SelectedLogLevel = LogLevel.Critical;
                logger.Log(LogLevel.Error, null, "ERROR_BELOW_MINIMUM");
                logger.Log(LogLevel.Critical, null, "CRITICAL_ENABLED");
            }

            var content = File.ReadAllText(Directory.GetFiles(directory).Single());
            Assert.Contains("DEBUG_ENABLED", content);
            Assert.Contains("CRITICAL_ENABLED", content);
            Assert.DoesNotContain("DEBUG_BEFORE", content);
            Assert.DoesNotContain("TRACE_BELOW_MINIMUM", content);
            Assert.DoesNotContain("ERROR_BELOW_MINIMUM", content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Capture_border_restores_saved_values_and_defaults_to_off(string? raw, bool expected)
    {
        var settings = new RecordingSettings();
        settings.Values["ShowCaptureBorder"] = raw;
        using var viewModel = Create(settings, new RecordingThemeStore());

        Assert.Equal(expected, viewModel.ShowCaptureBorder);
        Assert.Equal(0, settings.WriteCount);
    }

    [Fact]
    public void Capture_border_changes_persist_and_notify_only_once_per_change()
    {
        var settings = new RecordingSettings();
        using var viewModel = Create(settings, new RecordingThemeStore());
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        foreach (var enabled in new[] { true, false })
        {
            changes.Clear();
            viewModel.ShowCaptureBorder = enabled;
            viewModel.ShowCaptureBorder = enabled;

            Assert.Equal(enabled ? "true" : "false", settings.GetValue("ShowCaptureBorder"));
            Assert.Equal(1, changes.Count(name => name == nameof(SettingsPageViewModel.ShowCaptureBorder)));
            using var restored = Create(settings, new RecordingThemeStore());
            Assert.Equal(enabled, restored.ShowCaptureBorder);
        }
        Assert.Equal(2, settings.WriteCount);
    }

    [Fact]
    public void Failed_capture_border_save_keeps_the_committed_value()
    {
        var settings = new RecordingSettings { FailWrites = true };
        using var viewModel = Create(settings, new RecordingThemeStore());

        viewModel.ShowCaptureBorder = true;

        Assert.False(viewModel.ShowCaptureBorder);
        Assert.Null(settings.GetValue("ShowCaptureBorder"));
        Assert.NotEmpty(viewModel.CaptureBorderError);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(0.75, "0.75")]
    [InlineData(-1, "0")]
    [InlineData(2, "1")]
    public void Popup_transparency_saves_invariant_values_and_notifies_the_renderer(double requested, string expected)
    {
        var settings = new RecordingSettings();
        using var page = Create(settings, new RecordingThemeStore());
        Assert.Equal(0.3, page.OverlayAppearance.PopupBackgroundTransparency);
        var notifications = 0;
        page.OverlayAppearance.Changed += (_, _) => notifications++;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            page.OverlayAppearance.PopupBackgroundTransparency = requested;
            Assert.Equal(expected, settings.GetValue("PhrasePopupBackgroundTransparency"));
            Assert.Equal(1, notifications);
            page.OverlayAppearance.ResetColorsCommand.Execute(null);
            using var restored = Create(settings, new RecordingThemeStore());
            Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), restored.OverlayAppearance.PopupBackgroundTransparency);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public void Failed_popup_transparency_save_preserves_the_committed_preference()
    {
        var settings = new RecordingSettings { FailWrites = true };
        using var page = Create(settings, new RecordingThemeStore());
        var notifications = 0;
        page.OverlayAppearance.Changed += (_, _) => notifications++;

        page.OverlayAppearance.PopupBackgroundTransparency = 0.75;

        Assert.Equal(0.3, page.OverlayAppearance.PopupBackgroundTransparency);
        Assert.Null(settings.GetValue("PhrasePopupBackgroundTransparency"));
        Assert.NotEmpty(page.OverlayAppearance.PopupBackgroundTransparencyError);
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData(0.1, "0.5")]
    [InlineData(2.25, "2.25")]
    [InlineData(10, "3")]
    public void Outline_width_uses_a_bounded_range_and_invariant_storage(double requested, string expected)
    {
        var settings = new RecordingSettings();
        using var page = Create(settings, new RecordingThemeStore());
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            page.OverlayAppearance.OutlineThickness = requested;
            Assert.Equal(expected, settings.GetValue(OverlayAppearanceSettings.OutlineThicknessKey));
            Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), page.OverlayAppearance.OutlineThickness);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public void Appearance_colors_save_independently_and_reset_preserves_other_preferences()
    {
        var settings = new RecordingSettings();
        settings.SetValue("ApiKey", "test-key");
        using var page = Create(settings, new RecordingThemeStore());
        var viewModel = page.OverlayAppearance;
        var high = viewModel.Colors.Single(color => color.Key == "OverlayPitchHighColor");
        var notifications = 0;
        viewModel.Changed += (_, _) => notifications++;

        Assert.True(viewModel.TrySaveColor(high, "#abcdef"));
        Assert.Equal("#ABCDEF", high.Value);
        Assert.Equal("#4488FF", viewModel.Appearance.PitchLowColor);
        Assert.False(viewModel.TrySaveColor(high, "#123"));
        Assert.Equal("#ABCDEF", settings.GetValue(high.Key));
        Assert.NotEmpty(high.ErrorMessage);
        viewModel.OutlineEnabled = false;
        viewModel.OutlineColorMode = OverlayOutlineColorMode.White;
        viewModel.OutlineThickness = 2.25;
        viewModel.ResetColorsCommand.Execute(null);

        Assert.Equal(new OverlayAppearance(OutlineEnabled: false,
            OutlineColorMode: OverlayOutlineColorMode.White, OutlineThickness: 2.25), viewModel.Appearance);
        Assert.Equal("false", settings.GetValue("OverlayOutlineEnabled"));
        Assert.Equal("test-key", settings.GetValue("ApiKey"));
        Assert.Equal(5, notifications);
        using var reloaded = Create(settings, new RecordingThemeStore());
        Assert.Equal(viewModel.Appearance, reloaded.OverlayAppearance.Appearance);
    }

    [Fact]
    public void Appearance_save_failure_preserves_current_colors_and_does_not_notify_renderer()
    {
        var settings = new RecordingSettings();
        using var page = Create(settings, new RecordingThemeStore());
        var viewModel = page.OverlayAppearance;
        var high = viewModel.Colors[1];
        var original = viewModel.Appearance;
        var notifications = 0;
        viewModel.Changed += (_, _) => notifications++;
        settings.FailWrites = true;

        Assert.False(viewModel.TrySaveColor(high, "#000000"));
        viewModel.OutlineEnabled = false;
        viewModel.OutlineColorMode = OverlayOutlineColorMode.Black;
        viewModel.OutlineThickness = 2;
        viewModel.ResetColorsCommand.Execute(null);

        Assert.Same(original, viewModel.Appearance);
        Assert.NotEmpty(high.ErrorMessage);
        Assert.NotEmpty(viewModel.OutlineError);
        Assert.NotEmpty(viewModel.OutlineColorModeError);
        Assert.NotEmpty(viewModel.OutlineThicknessError);
        Assert.NotEmpty(viewModel.ResetStatus);
        Assert.Null(settings.GetValue(high.Key));
        Assert.Equal(0, notifications);
    }

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
        Assert.Contains("Next recognition language:", viewModel.EffectiveExplanationLanguage);
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
        using var viewModel = new SettingsPageViewModel(settings, theme, language, llm, localizer, language,
            new OverlayAppearanceViewModel(settings, localizer));
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
        var viewModel = new SettingsPageViewModel(settings, new RecordingThemeStore(), language, llm, localizer, language,
            new OverlayAppearanceViewModel(settings, localizer));
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
            new LlmSettingsViewModel(settings, new EmptyCatalog(), localizer), localizer, language,
            new OverlayAppearanceViewModel(settings, localizer));
    }

    private sealed class RecordingSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        public Dictionary<string, string?> Values { get; } = [];
        public int WriteCount { get; private set; }
        public bool FailWrites { get; set; }
        public override bool TryUpdate(IReadOnlyDictionary<string, string?> expected, IReadOnlyDictionary<string, string?> changes)
        {
            if (FailWrites)
                throw new System.IO.IOException("Simulated storage failure.");
            return base.TryUpdate(expected, changes);
        }
        public override string? GetValue(string key) => Values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value)
        {
            if (FailWrites)
                throw new System.IO.IOException("Simulated storage failure.");
            Values[key] = value;
            WriteCount++;
        }
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
