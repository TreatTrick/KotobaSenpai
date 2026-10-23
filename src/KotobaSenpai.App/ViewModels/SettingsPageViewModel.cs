using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.ViewModels;

public sealed partial class ThemeModeChoice(AppThemeMode mode, string name) : ObservableObject
{
    public AppThemeMode Mode { get; } = mode;

    [ObservableProperty]
    private string _name = name;
}

/// <summary>Owns page preferences and composes shared language and LLM settings state.</summary>
public sealed partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    public const double MinimumFuriganaFontScale = 0.1;
    public const double MaximumFuriganaFontScale = 0.5;

    private readonly ISettingsService _settings;
    private readonly IThemePreferenceStore _themePreferences;
    private readonly IStringLocalizer _localizer;
    private double _furiganaFontScale;
    private bool _disposed;

    public SettingsPageViewModel(ISettingsService settings, IThemePreferenceStore themePreferences,
        ILanguagePreferences language, LlmSettingsViewModel llm, IStringLocalizer localizer)
    {
        _settings = settings;
        _themePreferences = themePreferences;
        _localizer = localizer;
        Language = language;
        Llm = llm;
        _selectedTheme = themePreferences.Load() ?? AppThemeMode.Auto;
        _furiganaFontScale = NormalizeFontScale(FuriganaFontSettings.ResolveFontScale(settings.GetValue(FuriganaFontSettings.FontScaleKey)));
        ThemeModes = [new(AppThemeMode.Auto, string.Empty), new(AppThemeMode.Light, string.Empty), new(AppThemeMode.Dark, string.Empty)];
        RefreshThemeNames();
        _localizer.CultureChanged += OnCultureChanged;
    }

    public ILanguagePreferences Language { get; }
    public LlmSettingsViewModel Llm { get; }
    public IReadOnlyList<ThemeModeChoice> ThemeModes { get; }

    [ObservableProperty]
    private AppThemeMode _selectedTheme;

    public double FuriganaFontScale
    {
        get => _furiganaFontScale;
        set
        {
            if (SetProperty(ref _furiganaFontScale, NormalizeFontScale(value)))
                _settings.SetValue(FuriganaFontSettings.FontScaleKey, _furiganaFontScale.ToString(CultureInfo.InvariantCulture));
        }
    }

    partial void OnSelectedThemeChanged(AppThemeMode value) => _themePreferences.Save(value);

    private static double NormalizeFontScale(double value)
        => Math.Clamp(double.IsFinite(value) && value > 0 ? value : FuriganaFontSettings.DefaultFontScale,
            MinimumFuriganaFontScale, MaximumFuriganaFontScale);

    private void RefreshThemeNames()
    {
        foreach (var choice in ThemeModes)
            choice.Name = _localizer.Get(choice.Mode switch
            {
                AppThemeMode.Light => ResourceKeys.ThemeMode_Light,
                AppThemeMode.Dark => ResourceKeys.ThemeMode_Dark,
                _ => ResourceKeys.ThemeMode_Auto,
            });
    }

    private void OnCultureChanged(object? sender, EventArgs e) => RefreshThemeNames();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
        Llm.Dispose();
    }
}
