using System.Windows.Controls;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Views;

public partial class MainContentView : UserControl
{
    public MainContentView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as MainWindowViewModel)?.Support?.NotifyMainViewReady();
    }
}
