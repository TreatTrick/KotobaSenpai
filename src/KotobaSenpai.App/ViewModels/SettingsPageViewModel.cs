using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.PhraseAnalysis;

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
    private readonly IExplanationLanguagePreferences _explanationPreferences;
    private string? _explanationStatusKey;
    private double _furiganaFontScale;
    private bool _disposed;

    public SettingsPageViewModel(ISettingsService settings, IThemePreferenceStore themePreferences,
        ILanguagePreferences language, LlmSettingsViewModel llm, IStringLocalizer localizer,
        IExplanationLanguagePreferences explanationLanguage, OverlayAppearanceViewModel overlayAppearance)
    {
        _settings = settings;
        _themePreferences = themePreferences;
        _localizer = localizer;
        _explanationPreferences = explanationLanguage;
        _explanationLanguage = explanationLanguage.CustomLanguage ?? string.Empty;
        Language = language;
        Llm = llm;
        OverlayAppearance = overlayAppearance;
        _selectedTheme = themePreferences.Load() ?? AppThemeMode.Auto;
        _furiganaFontScale = NormalizeFontScale(FuriganaFontSettings.ResolveFontScale(settings.GetValue(FuriganaFontSettings.FontScaleKey)));
        ThemeModes = [new(AppThemeMode.Auto, string.Empty), new(AppThemeMode.Light, string.Empty), new(AppThemeMode.Dark, string.Empty)];
        RefreshThemeNames();
        _localizer.CultureChanged += OnCultureChanged;
        _explanationPreferences.EffectiveLanguageChanged += OnExplanationLanguageChanged;
    }

    public ILanguagePreferences Language { get; }
    public LlmSettingsViewModel Llm { get; }
    public OverlayAppearanceViewModel OverlayAppearance { get; }
    public IReadOnlyList<ThemeModeChoice> ThemeModes { get; }

    [ObservableProperty]
    private AppThemeMode _selectedTheme;

    [ObservableProperty]
    private string _explanationLanguage;

    public string EffectiveExplanationLanguage => _localizer.Get(ResourceKeys.ExplanationLanguage_Current,
        _explanationPreferences.CustomLanguage ?? Language.CurrentCulture.NativeName);

    public string ExplanationLanguageStatus
        => _explanationStatusKey is null ? string.Empty : _localizer.Get(_explanationStatusKey);

    [RelayCommand]
    private void SaveExplanationLanguage()
    {
        try
        {
            if (_explanationPreferences.TrySave(ExplanationLanguage))
            {
                ExplanationLanguage = _explanationPreferences.CustomLanguage ?? string.Empty;
                _explanationStatusKey = ResourceKeys.ExplanationLanguage_Saved;
            }
            else
                _explanationStatusKey = ResourceKeys.ExplanationLanguage_Invalid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _explanationStatusKey = ResourceKeys.ExplanationLanguage_SaveFailed;
        }
        OnPropertyChanged(nameof(EffectiveExplanationLanguage));
        OnPropertyChanged(nameof(ExplanationLanguageStatus));
    }

    private void OnExplanationLanguageChanged(object? sender, EventArgs e)
    {
        _explanationStatusKey = ResourceKeys.ExplanationLanguage_Saved;
        OnPropertyChanged(nameof(EffectiveExplanationLanguage));
        OnPropertyChanged(nameof(ExplanationLanguageStatus));
    }

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

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshThemeNames();
        OverlayAppearance.RefreshLanguage();
        OnPropertyChanged(nameof(EffectiveExplanationLanguage));
        OnPropertyChanged(nameof(ExplanationLanguageStatus));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
        _explanationPreferences.EffectiveLanguageChanged -= OnExplanationLanguageChanged;
        Llm.Dispose();
    }
}
