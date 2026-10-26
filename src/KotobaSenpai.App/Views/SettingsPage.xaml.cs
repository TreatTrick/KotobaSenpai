using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Runtime.InteropServices;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Views;

/// <summary>Synchronizes the password control with the settings page's LLM state.</summary>
public partial class SettingsPage : UserControl
{
    private LlmSettingsViewModel? _observedSettings;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ObserveLlmSettings();
        Unloaded += (_, _) => StopObservingLlmSettings();
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded)
                ObserveLlmSettings();
        };
    }

    private void ObserveLlmSettings()
    {
        StopObservingLlmSettings();
        _observedSettings = (DataContext as SettingsPageViewModel)?.Llm;
        if (_observedSettings is not null)
            _observedSettings.PropertyChanged += OnLlmSettingsChanged;
        SyncLlmApiKeyPassword();
    }

    private void StopObservingLlmSettings()
    {
        if (_observedSettings is not null)
            _observedSettings.PropertyChanged -= OnLlmSettingsChanged;
        _observedSettings = null;
    }

    private void OnLlmSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LlmSettingsViewModel.ApiKey))
            SyncLlmApiKeyPassword();
    }

    private void LlmApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsPageViewModel)?.Llm is { } settings
            && !string.Equals(settings.ApiKey, LlmApiKeyPasswordBox.Password, StringComparison.Ordinal))
            settings.ApiKey = LlmApiKeyPasswordBox.Password;
    }

    private void SyncLlmApiKeyPassword()
    {
        if ((DataContext as SettingsPageViewModel)?.Llm is { } settings
            && !string.Equals(settings.ApiKey, LlmApiKeyPasswordBox.Password, StringComparison.Ordinal))
            LlmApiKeyPasswordBox.Password = settings.ApiKey;
    }

    private void CopyAgentPrompt_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsPageViewModel)?.Llm is not { } settings)
            return;
        var path = Path.Combine(AppContext.BaseDirectory, "KotobaSenpai.Cli.exe");
        var prompt = settings.CreateAgentPrompt(File.Exists(path) ? path : null);
        if (prompt is null)
            return;
        try
        {
            Clipboard.SetText(prompt);
            settings.ReportPromptCopy(true);
        }
        catch (ExternalException) { settings.ReportPromptCopy(false); }
    }

    private void CopyConfigurationError_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsPageViewModel)?.Llm is not { } settings
            || string.IsNullOrWhiteSpace(settings.ConfigurationErrorDetails))
            return;
        try
        {
            Clipboard.SetText(settings.ConfigurationErrorDetails);
            settings.ReportErrorCopy(true);
        }
        catch (ExternalException) { settings.ReportErrorCopy(false); }
    }
}
