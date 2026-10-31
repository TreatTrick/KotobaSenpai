using System.IO;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.ViewModels;

public sealed partial class OverlayColorChoice(string key, IStringLocalizer localizer, string value) : ObservableObject
{
    public string Key { get; } = key;
    public string Label => localizer.Get(Key);
    public string Description => localizer.Get(Key + "_Description");
    [ObservableProperty] private string _value = value;
    private string? _errorKey;
    public string ErrorMessage => _errorKey is null ? string.Empty : localizer.Get(_errorKey);

    internal void SetError(string? key)
    {
        _errorKey = key;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ErrorMessage));
    }
}

public sealed partial class OutlineColorModeChoice(OverlayOutlineColorMode mode) : ObservableObject
{
    public OverlayOutlineColorMode Mode { get; } = mode;
    [ObservableProperty] private string _name = string.Empty;
}

/// <summary>Saves overlay preferences atomically and publishes only successfully saved appearances.</summary>
public sealed partial class OverlayAppearanceViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IStringLocalizer _localizer;
    private string? _outlineErrorKey;
    private string? _outlineColorModeErrorKey;
    private string? _outlineThicknessErrorKey;
    private string? _popupBackgroundTransparencyErrorKey;
    private string? _resetStatusKey;

    public OverlayAppearanceViewModel(ISettingsService settings, IStringLocalizer localizer)
    {
        _settings = settings;
        _localizer = localizer;
        _appearance = OverlayAppearanceSettings.Load(settings);
        Colors = OverlayAppearanceSettings.Colors(Appearance)
            .Select(entry => new OverlayColorChoice(entry.Key, localizer, entry.Value!)).ToArray();
        OutlineColorModes = [new(OverlayOutlineColorMode.Auto), new(OverlayOutlineColorMode.Black), new(OverlayOutlineColorMode.White)];
        RefreshLanguage();
    }

    public IReadOnlyList<OverlayColorChoice> Colors { get; }
    public IReadOnlyList<OutlineColorModeChoice> OutlineColorModes { get; }
    [ObservableProperty] private OverlayAppearance _appearance;
    public event EventHandler? Changed;
    public string OutlineError => _outlineErrorKey is null ? string.Empty : _localizer.Get(_outlineErrorKey);
    public string OutlineColorModeError => _outlineColorModeErrorKey is null ? string.Empty : _localizer.Get(_outlineColorModeErrorKey);
    public string OutlineThicknessError => _outlineThicknessErrorKey is null ? string.Empty : _localizer.Get(_outlineThicknessErrorKey);
    public string PopupBackgroundTransparencyError => _popupBackgroundTransparencyErrorKey is null
        ? string.Empty : _localizer.Get(_popupBackgroundTransparencyErrorKey);
    public string ResetStatus => _resetStatusKey is null ? string.Empty : _localizer.Get(_resetStatusKey);

    public bool OutlineEnabled
    {
        get => Appearance.OutlineEnabled;
        set
        {
            if (value == OutlineEnabled)
                return;
            _outlineErrorKey = Commit(new Dictionary<string, string?>
                { [OverlayAppearanceSettings.OutlineEnabledKey] = value ? "true" : "false" })
                ? null : ResourceKeys.OverlayAppearance_SaveFailed;
            OnPropertyChanged(nameof(OutlineEnabled));
            OnPropertyChanged(nameof(OutlineError));
        }
    }

    public OverlayOutlineColorMode OutlineColorMode
    {
        get => Appearance.OutlineColorMode;
        set
        {
            var mode = Enum.IsDefined(value) ? value : OverlayOutlineColorMode.Auto;
            if (mode == OutlineColorMode)
                return;
            _outlineColorModeErrorKey = Commit(new Dictionary<string, string?>
                { [OverlayAppearanceSettings.OutlineColorModeKey] = mode.ToString().ToLowerInvariant() })
                ? null : ResourceKeys.OverlayAppearance_SaveFailed;
            OnPropertyChanged(nameof(OutlineColorMode));
            OnPropertyChanged(nameof(OutlineColorModeError));
        }
    }

    public double OutlineThickness
    {
        get => Appearance.OutlineThickness;
        set
        {
            var thickness = Math.Clamp(double.IsFinite(value) ? value : OverlayAppearanceSettings.Default.OutlineThickness,
                OverlayAppearanceSettings.MinimumOutlineThickness, OverlayAppearanceSettings.MaximumOutlineThickness);
            if (thickness == OutlineThickness)
                return;
            _outlineThicknessErrorKey = Commit(new Dictionary<string, string?>
                { [OverlayAppearanceSettings.OutlineThicknessKey] = thickness.ToString(CultureInfo.InvariantCulture) })
                ? null : ResourceKeys.OverlayAppearance_SaveFailed;
            OnPropertyChanged(nameof(OutlineThickness));
            OnPropertyChanged(nameof(OutlineThicknessError));
        }
    }

    public double PopupBackgroundTransparency
    {
        get => Appearance.PopupBackgroundTransparency;
        set
        {
            var transparency = Math.Clamp(double.IsFinite(value) ? value : OverlayAppearanceSettings.Default.PopupBackgroundTransparency, 0, 1);
            if (transparency == PopupBackgroundTransparency)
                return;
            _popupBackgroundTransparencyErrorKey = Commit(new Dictionary<string, string?>
                { [OverlayAppearanceSettings.PopupBackgroundTransparencyKey] = transparency.ToString(CultureInfo.InvariantCulture) })
                ? null : ResourceKeys.OverlayAppearance_SaveFailed;
            OnPropertyChanged(nameof(PopupBackgroundTransparency));
            OnPropertyChanged(nameof(PopupBackgroundTransparencyError));
        }
    }

    public bool TrySaveColor(OverlayColorChoice choice, string? raw)
    {
        if (!Colors.Contains(choice))
            throw new ArgumentException("The color choice does not belong to this settings page.", nameof(choice));
        if (!OverlayAppearanceSettings.TryNormalizeColor(raw, out var color))
        {
            choice.SetError(ResourceKeys.OverlayAppearance_InvalidColor);
            return false;
        }
        var saved = Commit(new Dictionary<string, string?> { [choice.Key] = color });
        choice.SetError(saved ? null : ResourceKeys.OverlayAppearance_SaveFailed);
        return saved;
    }

    [RelayCommand]
    private void ResetColors()
    {
        var saved = Commit(OverlayAppearanceSettings.Colors(OverlayAppearanceSettings.Default));
        _resetStatusKey = saved ? ResourceKeys.OverlayAppearance_ColorsReset : ResourceKeys.OverlayAppearance_SaveFailed;
        if (saved)
            foreach (var color in Colors)
                color.SetError(null);
        OnPropertyChanged(nameof(ResetStatus));
    }

    private bool Commit(IReadOnlyDictionary<string, string?> changes)
    {
        try
        {
            if (!_settings.TryUpdate(_settings.GetSnapshot(changes.Keys), changes))
                return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        Appearance = OverlayAppearanceSettings.Load(_settings);
        _resetStatusKey = null;
        OnPropertyChanged(nameof(ResetStatus));
        var values = OverlayAppearanceSettings.Colors(Appearance);
        foreach (var choice in Colors)
            choice.Value = values[choice.Key]!;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal void RefreshLanguage()
    {
        foreach (var color in Colors)
            color.RefreshLanguage();
        foreach (var mode in OutlineColorModes)
            mode.Name = _localizer.Get(mode.Mode switch
            {
                OverlayOutlineColorMode.Black => ResourceKeys.OverlayAppearance_OutlineBlack,
                OverlayOutlineColorMode.White => ResourceKeys.OverlayAppearance_OutlineWhite,
                _ => ResourceKeys.OverlayAppearance_OutlineAuto,
            });
        OnPropertyChanged(nameof(OutlineError));
        OnPropertyChanged(nameof(OutlineColorModeError));
        OnPropertyChanged(nameof(OutlineThicknessError));
        OnPropertyChanged(nameof(PopupBackgroundTransparencyError));
        OnPropertyChanged(nameof(ResetStatus));
    }
}
