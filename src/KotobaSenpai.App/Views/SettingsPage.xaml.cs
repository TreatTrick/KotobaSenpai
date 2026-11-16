using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.ViewModels;
using Microsoft.Win32;

namespace KotobaSenpai.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private async void ExportDiagnosticBundle_OnClick(object sender, RoutedEventArgs e)
    {
        var localizer = LocalizationHost.Find(this)?.Localizer;
        if (localizer is null) return;

        if (DataContext is not SettingsPageViewModel viewModel) return;
        try
        {
            var files = viewModel.CollectDiagnosticBundleFiles();
            if (files.Count == 0)
            {
                DiagnosticBundleDialog.ShowNotice(Window.GetWindow(this),
                    localizer.Get(ResourceKeys.DiagnosticExport_Title),
                    localizer.Get(ResourceKeys.DiagnosticExport_NoFiles),
                    localizer.Get(ResourceKeys.DiagnosticExport_Close));
                return;
            }

            var owner = Window.GetWindow(this);
            var confirmed = DiagnosticBundleDialog.ShowConfirmation(owner,
                localizer.Get(ResourceKeys.DiagnosticExport_ConfirmTitle),
                localizer.Get(ResourceKeys.DiagnosticExport_ContentHeading),
                viewModel.BuildDiagnosticBundleContents(files),
                localizer.Get(ResourceKeys.DiagnosticExport_LocalOnly),
                localizer.Get(ResourceKeys.DiagnosticExport_Continue),
                localizer.Get(ResourceKeys.DiagnosticExport_ConfirmButton),
                localizer.Get(ResourceKeys.DiagnosticExport_CancelButton));
            if (!confirmed) return;

            var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".zip",
                FileName = $"kotobasenpai-diagnostics-{DateTime.Now:yyyy-MM-dd}.zip",
                Filter = localizer.Get(ResourceKeys.DiagnosticExport_FileFilter),
                OverwritePrompt = true,
                Title = localizer.Get(ResourceKeys.DiagnosticExport_SaveTitle),
            };
            var selected = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (selected != true) return;

            var exported = await viewModel.ExportDiagnosticBundleAsync(files, dialog.FileName);
            if (!exported)
            {
                DiagnosticBundleDialog.ShowNotice(owner, localizer.Get(ResourceKeys.DiagnosticExport_Title),
                    localizer.Get(ResourceKeys.DiagnosticExport_Failed), localizer.Get(ResourceKeys.DiagnosticExport_Close));
                return;
            }
            DiagnosticBundleDialog.ShowNotice(owner, localizer.Get(ResourceKeys.DiagnosticExport_Title),
                localizer.Get(ResourceKeys.DiagnosticExport_Success, dialog.FileName),
                localizer.Get(ResourceKeys.DiagnosticExport_Close));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or Win32Exception)
        {
            viewModel.LogDiagnosticExportFailure(ex);
            DiagnosticBundleDialog.ShowNotice(Window.GetWindow(this), localizer.Get(ResourceKeys.DiagnosticExport_Title),
                localizer.Get(ResourceKeys.DiagnosticExport_Failed), localizer.Get(ResourceKeys.DiagnosticExport_Close));
        }
    }
}
