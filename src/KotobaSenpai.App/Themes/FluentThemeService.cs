using System.Windows;
using KotobaSenpai.App.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App.Themes;

/// <summary>
/// 视图层主题服务：把主题模式（Auto/Light/Dark）解析为 WPF-UI Fluent 主题并应用，
/// 持久化用户选择，并在 Auto 模式下经 <see cref="SystemThemeWatcher"/> 跟随 Windows 系统主题。
/// 仅存在于 App 视图层（引用 Wpf.Ui 与 System.Windows），不注入 ViewModel。
/// 不实现 IDisposable：SystemThemeWatcher 随窗口关闭/进程退出由 WPF-UI 自动清理，
/// 退出时显式 UnWatch 会因窗口句柄已销毁而抛 InvalidOperationException。
/// </summary>
public sealed class FluentThemeService
{
    private readonly IThemePreferenceStore _store;
    private Window? _window;
    private AppThemeMode _mode = AppThemeMode.Auto;

    /// <summary>当前主题模式。</summary>
    public AppThemeMode CurrentMode => _mode;

    public FluentThemeService(IThemePreferenceStore store) => _store = store;

    /// <summary>启动时绑定主窗口、应用持久化（或缺省 Auto）模式。须在窗口句柄创建后调用（如 OnSourceInitialized）。</summary>
    public void Initialize(Window window)
    {
        _window = window;
        SetMode(_store.Load(), persist: false);
    }

    /// <summary>设置主题模式并应用；persist=true 时持久化。null 视为 Auto。</summary>
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
                // 跟随系统：应用当前系统主题并订阅系统深浅变化。
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
