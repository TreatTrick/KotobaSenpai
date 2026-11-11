using System.ComponentModel;
using System.Windows;
using KotobaSenpai.App.Shortcuts;
using KotobaSenpai.App.Support;
using KotobaSenpai.App.Themes;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Startup;

/// <summary>Owns window startup, interaction wiring and orderly shutdown for one desktop scope.</summary>
internal sealed class DesktopSession(MainWindowViewModel main, ReadingShortcutCoordinator shortcuts,
    RecognitionStartup recognition, FluentThemeService theme, IStringLocalizer localizer, ILogger logger) : IDisposable
{
    private SupportCoordinator? _support;
    private MainWindow? _window;
    private bool _closing;
    private bool _closeAllowed;
    private bool _disposed;

    public void Start(Application application)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        recognition.Prepared += OnRecognitionPrepared;
        shortcuts.Changed += OnShortcutStateChanged;
        shortcuts.Start();
        main.Onboarding.PropertyChanged += OnReadinessChanged;
        main.Settings.Llm.PropertyChanged += OnReadinessChanged;
        main.Dictionary.PropertyChanged += OnReadinessChanged;
        main.Settings.OverlayAppearance.Changed += OnOverlayAppearanceChanged;
        main.About.InstallStarted += OnUpdateInstallStarted;
        _window = new MainWindow { ThemeService = theme, DataContext = main };
        application.MainWindow = _window;
        _support = new SupportCoordinator(main.Support!, main, _window, localizer, logger);
        _window.Closing += OnMainWindowClosing;
        _window.Show();
        _ = main.Onboarding.InitializeCommand.ExecuteAsync(null);
    }

    private void OnRecognitionPrepared(object? sender, RecognitionRuntime runtime)
    {
        if (_disposed || _closing) return;
        main.Recognition = runtime.Page;
        runtime.Page.Actions.IsInteractionEnabled = () => !_closing && !_disposed
            && main.Onboarding.IsMain && !shortcuts.IsRecording;
        shortcuts.Attach(runtime.Page.Actions);
        runtime.Page.Selection.PropertyChanged += OnReadinessChanged;
    }

    private void OnReadinessChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (!ReferenceEquals(sender, main.Onboarding)) main.Onboarding.Refresh();
        main.Recognition?.RefreshInteraction();
    }

    private void OnShortcutStateChanged(object? sender, EventArgs e) => main.Recognition?.RefreshInteraction();
    private void OnOverlayAppearanceChanged(object? sender, EventArgs e) => recognition.RefreshAppearance();
    private void OnUpdateInstallStarted(object? sender, EventArgs e) => _window?.Close();

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed) return;
        e.Cancel = true;
        if (_closing || sender is not Window window) return;
        _closing = true;
        recognition.Dispose();
        shortcuts.Detach();
        _support?.Dispose();
        window.IsEnabled = false;
        try { await main.StopAsync(); }
        catch (Exception exception) { logger.LogError(exception, "Failed to finish cancellation during shutdown."); }
        _closeAllowed = true;
        _ = window.Dispatcher.BeginInvoke(() => window.Close());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        recognition.Dispose();
        recognition.Prepared -= OnRecognitionPrepared;
        shortcuts.Changed -= OnShortcutStateChanged;
        shortcuts.Detach();
        _support?.Dispose();
        main.Onboarding.PropertyChanged -= OnReadinessChanged;
        main.Settings.Llm.PropertyChanged -= OnReadinessChanged;
        main.Dictionary.PropertyChanged -= OnReadinessChanged;
        main.Settings.OverlayAppearance.Changed -= OnOverlayAppearanceChanged;
        main.About.InstallStarted -= OnUpdateInstallStarted;
        if (main.Selection is not null) main.Selection.PropertyChanged -= OnReadinessChanged;
        if (_window is not null) _window.Closing -= OnMainWindowClosing;
        main.Dispose();
    }
}
