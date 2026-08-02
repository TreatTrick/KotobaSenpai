using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.Themes;
using KotobaSenpai.App.ViewModels;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App;

/// <summary>
/// 视图：仅保留与平台相关的少量代码。
/// 在窗口句柄创建后把它告诉视图模型（用于排除自身），再触发一次刷新；其余逻辑全部在 ViewModel。
/// 暴露 <see cref="LanguageService"/> 供语言选择 ComboBox 绑定、<see cref="ThemeService"/> 供主题选择 ComboBox 调用
/// （二者均不入 ViewModel，保持依赖方向纯净）。
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ExcludeHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            viewModel.RefreshCommand.Execute(null);
        }

        // 主题：在窗口句柄就绪后应用持久化（或缺省 Auto）模式并绑定 OS 跟随，再同步下拉框选中项。
        ThemeService?.Initialize(this);
        SyncThemeModeComboBox();

        if (LocalizationHost.Localizer is { } localizer)
            localizer.CultureChanged += (_, _) => SyncThemeModeComboBox();
    }

    /// <summary>主题模式 ComboBox 选择变化：解析 Tag 调用主题服务。</summary>
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

    /// <summary>按当前模式选中主题 ComboBox 对应项（带重入保护，避免编程式选中触发回写）。</summary>
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
