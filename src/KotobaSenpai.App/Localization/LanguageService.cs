using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// Manages the app's UI language: resolves the startup culture (persisted preference -> Simplified Chinese when the system is Chinese, otherwise English),
/// sets the global <c>CurrentUICulture</c> and the <see cref="ResourceManagerStringLocalizer"/>,
/// persists/restores the preference, and exposes <see cref="AvailableCultures"/> and <see cref="CurrentCulture"/> for binding.
/// Default UI: Simplified Chinese when the system is Chinese, otherwise English; English is the neutral fallback.
/// </summary>
public sealed partial class LanguageService : ObservableObject
{
    /// <summary>Supported cultures: Simplified Chinese first by default, English as the fallback.</summary>
    public IReadOnlyList<CultureInfo> AvailableCultures { get; } = [new CultureInfo("zh-CN"), new CultureInfo("en")];

    private readonly ResourceManagerStringLocalizer _localizer;
    private readonly ILanguagePreferenceStore _store;
    private readonly Func<CultureInfo> _osCultureProvider;
    private readonly Action<CultureInfo> _applyGlobalCulture;
    private bool _applying;

    [ObservableProperty]
    private CultureInfo _currentCulture;

    public LanguageService(
        ResourceManagerStringLocalizer localizer,
        ILanguagePreferenceStore store,
        Func<CultureInfo>? osCultureProvider = null,
        Action<CultureInfo>? applyGlobalCulture = null)
    {
        _localizer = localizer;
        _store = store;
        _osCultureProvider = osCultureProvider ?? (() => CultureInfo.CurrentUICulture);
        _applyGlobalCulture = applyGlobalCulture ?? ApplyGlobalCultureDefault;
        _currentCulture = AvailableCultures[0];
    }

    private static void ApplyGlobalCultureDefault(CultureInfo culture)
    {
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>Resolves and applies the culture at startup (does not persist; restores or takes the default).</summary>
    public void Initialize()
    {
        SetCulture(ResolveStartupCulture(), persist: false);
    }

    /// <summary>Switches the culture at runtime: applies the global and the localizer, and persists the user's choice.</summary>
    public void ChangeCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        SetCulture(culture, persist: true);
    }

    /// <summary>Triggered when the ComboBox two-way binding sets CurrentCulture; applies and persists.</summary>
    partial void OnCurrentCultureChanged(CultureInfo value)
    {
        if (_applying)
            return;
        SetCulture(value, persist: true);
    }

    private void SetCulture(CultureInfo culture, bool persist)
    {
        _applying = true;
        try
        {
            _applyGlobalCulture(culture);
            _localizer.ApplyCulture(culture);

            if (CurrentCulture.Name != culture.Name)
                CurrentCulture = culture;

            if (persist)
                _store.Save(culture.Name);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Resolves the startup culture: persisted preference -> Simplified Chinese when the system is Chinese, otherwise English.</summary>
    private CultureInfo ResolveStartupCulture()
    {
        var persisted = _store.Load();
        if (persisted is not null && TryMatchSupported(persisted, out var persistedCulture))
            return persistedCulture;

        // With no persisted preference (first launch): default to Simplified Chinese when the system UI culture is Chinese, otherwise default to English.
        return IsChinese(_osCultureProvider()) ? AvailableCultures[0] : AvailableCultures[1];
    }

    private static bool IsChinese(CultureInfo culture)
        => string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>Matches a supported culture by TwoLetterISOLanguageName (en* -> en, zh* -> zh-CN).</summary>
    private bool TryMatchSupported(CultureInfo culture, out CultureInfo matched)
    {
        foreach (var supported in AvailableCultures)
        {
            if (string.Equals(supported.TwoLetterISOLanguageName, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            {
                matched = supported;
                return true;
            }
        }

        matched = AvailableCultures[0];
        return false;
    }

    /// <summary>Matches a supported culture by culture name; an invalid name does not throw but returns false.</summary>
    private bool TryMatchSupported(string cultureName, out CultureInfo matched)
    {
        try
        {
            return TryMatchSupported(new CultureInfo(cultureName), out matched);
        }
        catch (CultureNotFoundException)
        {
            matched = AvailableCultures[0];
            return false;
        }
    }
}
