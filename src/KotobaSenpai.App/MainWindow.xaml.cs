using System.Windows;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App;

/// <summary>
/// 视图：仅保留与平台相关的少量代码。
/// 在窗口句柄创建后把它告诉视图模型（用于排除自身），再触发一次刷新；其余逻辑全部在 ViewModel。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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
