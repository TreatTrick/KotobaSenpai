using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.Themes;
using KotobaSenpai.App.ViewModels;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App;

/// <summary>
/// View: keeps only a small amount of platform-related code.
/// After the window handle is created it is handed to the view model (to exclude itself), then a refresh is triggered; all other logic lives in the ViewModel.
/// Exposes <see cref="LanguageService"/> for the language-selection ComboBox binding and <see cref="ThemeService"/> for the theme-selection ComboBox
/// (neither enters the ViewModel, keeping the dependency direction clean).
/// </summary>
public partial class MainWindow : FluentWindow
{
    private bool _syncing;

    public MainWindow() => InitializeComponent();

    public static readonly DependencyProperty LanguageServiceProperty =
        DependencyProperty.Register(
            nameof(LanguageService),
            typeof(LanguageService),
            typeof(MainWindow),
            new PropertyMetadata(null));

    public LanguageService? LanguageService
    {
        get => (LanguageService?)GetValue(LanguageServiceProperty);
        set => SetValue(LanguageServiceProperty, value);
    }

    public static readonly DependencyProperty ThemeServiceProperty =
        DependencyProperty.Register(
            nameof(ThemeService),
            typeof(FluentThemeService),
            typeof(MainWindow),
            new PropertyMetadata(null));

    public FluentThemeService? ThemeService
    {
        get => (FluentThemeService?)GetValue(ThemeServiceProperty);
        set => SetValue(ThemeServiceProperty, value);
    }

    public static readonly DependencyProperty InstallControllerProperty =
        DependencyProperty.Register(
            nameof(InstallController),
            typeof(UniDicInstallController),
            typeof(MainWindow),
            new PropertyMetadata(null));

    /// <summary>Dictionary install coordinator: drives the startup overlay (progress/error/retry). A view-level service, not in the ViewModel.</summary>
    public UniDicInstallController? InstallController
    {
        get => (UniDicInstallController?)GetValue(InstallControllerProperty);
        set => SetValue(InstallControllerProperty, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ExcludeHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            viewModel.RefreshCommand.Execute(null);
        }

        // Theme: after the window handle is ready, apply the persisted (or default Auto) mode, bind OS-follow, then sync the combo-box selection.
        ThemeService?.Initialize(this);
        SyncThemeModeComboBox();

        if (LocalizationHost.Localizer is { } localizer)
            localizer.CultureChanged += (_, _) => SyncThemeModeComboBox();
    }

    /// <summary>Theme-mode ComboBox selection change: parses the Tag and calls the theme service.</summary>
    private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeService is null || _syncing)
            return;

        if (e.AddedItems.Count > 0
            && e.AddedItems[0] is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<AppThemeMode>(tag, ignoreCase: true, out var mode))
        {
            ThemeService.SetMode(mode);
        }
    }

    /// <summary>Selects the theme ComboBox item matching the current mode (with re-entrancy protection to avoid a programmatic selection triggering a write-back).</summary>
    private void SyncThemeModeComboBox()
    {
        if (ThemeService is null)
            return;

        _syncing = true;
        try
        {
            foreach (var item in ThemeModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && Enum.TryParse<AppThemeMode>(tag, ignoreCase: true, out var mode)
                    && mode == ThemeService.CurrentMode)
                {
                    ThemeModeComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
