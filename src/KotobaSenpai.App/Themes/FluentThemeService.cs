using System.Windows;
using KotobaSenpai.App.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App.Themes;

/// <summary>
/// View-layer theme service: resolves a theme mode (Auto/Light/Dark) into a WPF-UI Fluent theme and applies it,
/// persists the user's choice, and in Auto mode follows the Windows system theme via <see cref="SystemThemeWatcher"/>.
/// Lives only in the App view layer (references Wpf.Ui and System.Windows); not injected into the ViewModel.
/// Does not implement IDisposable: SystemThemeWatcher is cleaned up automatically by WPF-UI when the window closes/process exits,
/// and an explicit UnWatch on exit would throw InvalidOperationException because the window handle is already destroyed.
/// </summary>
public sealed class FluentThemeService
{
    private readonly IThemePreferenceStore _store;
    private Window? _window;
    private AppThemeMode _mode = AppThemeMode.Auto;

    /// <summary>The current theme mode.</summary>
    public AppThemeMode CurrentMode => _mode;

    public FluentThemeService(IThemePreferenceStore store) => _store = store;

    /// <summary>At startup, binds the main window and applies the persisted (or default Auto) mode. Must be called after the window handle is created (e.g. OnSourceInitialized).</summary>
    public void Initialize(Window window)
    {
        _window = window;
        SetMode(_store.Load(), persist: false);
    }

    /// <summary>Sets the theme mode and applies it; persisted when persist=true. null is treated as Auto.</summary>
    public void SetMode(AppThemeMode? mode, bool persist = true)
    {
        _mode = mode ?? AppThemeMode.Auto;
        if (persist)
            _store.Save(_mode);
        ApplyResolvedTheme();
    }

    private void ApplyResolvedTheme()
    {
        if (_window is null)
            return;

        const WindowBackdropType backdrop = WindowBackdropType.Mica;
        switch (_mode)
        {
            case AppThemeMode.Auto:
                // Follow the system: apply the current system theme and subscribe to system light/dark changes.
                ApplicationThemeManager.ApplySystemTheme();
                SystemThemeWatcher.Watch(_window, backdrop, true);
                break;
            case AppThemeMode.Light:
                SystemThemeWatcher.UnWatch(_window);
                ApplicationThemeManager.Apply(ApplicationTheme.Light, backdrop, true);
                break;
            case AppThemeMode.Dark:
                SystemThemeWatcher.UnWatch(_window);
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, backdrop, true);
                break;
        }
    }
}
