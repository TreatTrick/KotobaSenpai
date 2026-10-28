using System.ComponentModel;
using System.Windows;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Themes;
using KotobaSenpai.App.ViewModels;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App;

/// <summary>Hosts page navigation and applies window-specific theme and native handle behavior.</summary>
public partial class MainWindow : FluentWindow
{
    private SettingsPageViewModel? _observedSettings;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => InitializeViewModel();
        Closed += (_, _) => StopObservingSettings();
    }

    public static readonly DependencyProperty ThemeServiceProperty =
        DependencyProperty.Register(nameof(ThemeService), typeof(FluentThemeService), typeof(MainWindow));

    public FluentThemeService? ThemeService
    {
        get => (FluentThemeService?)GetValue(ThemeServiceProperty);
        set => SetValue(ThemeServiceProperty, value);
    }

    public static readonly DependencyProperty InstallControllerProperty =
        DependencyProperty.Register(nameof(InstallController), typeof(UniDicInstallController), typeof(MainWindow));

    /// <summary>Drives the startup overlay while recognition services are being initialized.</summary>
    public UniDicInstallController? InstallController
    {
        get => (UniDicInstallController?)GetValue(InstallControllerProperty);
        set => SetValue(InstallControllerProperty, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService?.Initialize(this);
        InitializeViewModel();
    }

    private void InitializeViewModel()
    {
        StopObservingSettings();
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        _observedSettings = viewModel.Settings;
        _observedSettings.PropertyChanged += OnSettingsChanged;
        ApplySelectedTheme();


    }

    private void StopObservingSettings()
    {
        if (_observedSettings is not null)
            _observedSettings.PropertyChanged -= OnSettingsChanged;
        _observedSettings = null;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.SelectedTheme))
            ApplySelectedTheme();
    }

    private void ApplySelectedTheme()
    {
        if (_observedSettings is not null && ThemeService is not null
            && ThemeService.CurrentMode != _observedSettings.SelectedTheme)
            ThemeService.SetMode(_observedSettings.SelectedTheme, persist: false);
    }
}
