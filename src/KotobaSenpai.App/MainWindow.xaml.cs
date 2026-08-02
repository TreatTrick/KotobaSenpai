using System.Windows;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App;

/// <summary>
/// 视图：仅保留与平台相关的少量代码。
/// 在窗口句柄创建后把它告诉视图模型（用于排除自身），再触发一次刷新；其余逻辑全部在 ViewModel。
/// 暴露 <see cref="LanguageService"/> 供语言选择 ComboBox 绑定（语言服务不入 ViewModel，保持依赖方向纯净）。
/// </summary>
public partial class MainWindow : Window
{
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ExcludeHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            viewModel.RefreshCommand.Execute(null);
        }
    }
}
