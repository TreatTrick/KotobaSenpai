using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is AboutPageViewModel page) await page.EnsureCheckedAsync();
        };
    }

    private void OpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url }) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { (DataContext as AboutPageViewModel)?.ReportLinkFailure(exception); }
        e.Handled = true;
    }
}
