using System.Windows;
using System.Windows.Threading;
using KotobaSenpai.App.Composition;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.App;

/// <summary>Owns the WPF process lifetime and delegates graph construction to the composition root.</summary>
public partial class App : Application
{
    private DesktopComposition? _composition;
    private ILogger? _logger;
    private ConfigurationProcessLock? _settingsLease;
    private bool _handlingDispatcherError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settingsLease = ConfigurationProcessLock.TryAcquire();
        if (_settingsLease is null)
        {
            MessageBox.Show(new ResourceManagerStringLocalizer().Get("Agent_AppBusy"), "KotobaSenpai");
            Shutdown(4);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _composition = DesktopComposition.Create(Resources);
        _logger = _composition.Logger;
        _logger.LogInformation("App starting (v{0})", Environment.Version);
        ConfigureGlobalErrorHandling();
        _composition.Start(this);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _composition?.Dispose(); }
        finally
        {
            _settingsLease?.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>Registers global unhandled-exception fallbacks: all log at Error level; terminating exceptions log, show a localized notice to the user, then exit.</summary>
    private void ConfigureGlobalErrorHandling()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        // A modal notice pumps dispatcher messages; the same layout failure must not open another notice.
        if (_handlingDispatcherError) return;
        _handlingDispatcherError = true;
        // Log (FileLogger flushes on each write, so it is on disk before a crash), then prompt the user, then exit in a controlled way.
        Try(() => _logger?.LogError(e.Exception, "Unhandled exception on dispatcher thread"));
        try { ShowUnexpectedErrorNotice(); }
        finally { Shutdown(1); }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Try(() => _logger?.LogError(ex, $"Unhandled AppDomain exception (terminating={e.IsTerminating})"));

        if (e.IsTerminating)
            Try(() => Application.Current?.Dispatcher.Invoke(ShowUnexpectedErrorNotice));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Try(() => _logger?.LogError(e.Exception, "Unobserved task exception"));
        // Do not SetObserved, do not show a dialog, do not change the default (non-fatal) semantics.
    }

    /// <summary>Shows a generic localized error notice (without exposing the stack trace to the user). Skipped when the container is not ready.</summary>
    private void ShowUnexpectedErrorNotice()
    {
        var localizer = _composition?.Localizer;
        if (localizer is null)
            return;

        var title = localizer.Get(ResourceKeys.UnexpectedError_Title);
        var message = localizer.Get(ResourceKeys.UnexpectedError_Message);
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>A simple wrapper that swallows all exceptions, used on logging/fallback paths to ensure these paths never throw to the caller.</summary>
    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception) { }
    }
}
