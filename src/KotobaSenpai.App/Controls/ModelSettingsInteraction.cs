using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Controls;

/// <summary>Shares password synchronization and clipboard feedback between the two model editors.</summary>
internal sealed class ModelSettingsInteraction
{
    private readonly UserControl _view;
    private readonly PasswordBox _password;
    private LlmSettingsViewModel? _observed;
    private LlmSettingsViewModel? Settings => (_view.DataContext as SettingsPageViewModel)?.Llm;

    internal ModelSettingsInteraction(UserControl view, PasswordBox password)
    {
        _view = view;
        _password = password;
        view.Loaded += (_, _) => Observe();
        view.Unloaded += (_, _) => Detach();
        view.DataContextChanged += (_, _) => { if (view.IsLoaded) Observe(); };
        password.PasswordChanged += (_, _) =>
        {
            if (Settings is { } settings && settings.ApiKey != password.Password)
                settings.ApiKey = password.Password;
        };
    }

    private void Observe()
    {
        Detach();
        _observed = Settings;
        if (_observed is not null) _observed.PropertyChanged += OnSettingsChanged;
        SyncPassword();
    }

    private void Detach()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnSettingsChanged;
        _observed = null;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LlmSettingsViewModel.ApiKey)) SyncPassword();
    }

    private void SyncPassword()
    {
        if (Settings is { } settings && settings.ApiKey != _password.Password)
            _password.Password = settings.ApiKey;
    }

    internal void CopyAgentPrompt()
    {
        if (Settings is not { } settings) return;
        var path = Path.Combine(AppContext.BaseDirectory, "KotobaSenpai.Cli.exe");
        var prompt = settings.CreateAgentPrompt(File.Exists(path) ? path : null);
        if (prompt is null) return;
        try { Clipboard.SetText(prompt); settings.ReportPromptCopy(true); }
        catch (ExternalException) { settings.ReportPromptCopy(false); }
    }

    internal void CopyConfigurationError()
    {
        if (Settings is not { } settings || string.IsNullOrWhiteSpace(settings.ConfigurationErrorDetails)) return;
        try { Clipboard.SetText(settings.ConfigurationErrorDetails); settings.ReportErrorCopy(true); }
        catch (ExternalException) { settings.ReportErrorCopy(false); }
    }
}
