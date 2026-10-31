using KotobaSenpai.App.Onboarding;
using KotobaSenpai.App.Support;
using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Diagnostics;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.TargetWindow;
using System.IO;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using KotobaSenpai.App.Diagnostics;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Platform.Windows.Settings;
using KotobaSenpai.App.Themes;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Platform.Windows.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Platform.Windows.Dictionary;
using KotobaSenpai.Platform.Windows.Japanese;
using KotobaSenpai.Platform.Windows.Ocr;
using KotobaSenpai.Platform.Windows.Overlay;
using KotobaSenpai.Platform.Windows.AutoRecognition;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App;

/// <summary>
/// Composition root: registers dependencies by port at startup and lets the container assemble the object graph.
/// Platform adapters implement domain ports; application services and view models have their constructor dependencies resolved automatically by the container.
/// Localization ports (<see cref="IStringLocalizer"/>, etc.) live in Core, their implementations in App; at startup, before any localization resource
/// is accessed, <see cref="LanguageService"/> resolves and sets the UI culture.
/// The logging port <see cref="ILogger"/> lives in Core, its <see cref="FileLogger"/> implementation in App; at startup expired logs are cleaned up,
/// a global unhandled-exception handler is registered (logs, shows a localized notice to the user, then exits in a controlled way), and at runtime cleanup is triggered by the cross-midnight rollover event.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private FileLogger? _logger;
    private MainWindowViewModel? _mainViewModel;
    private SupportCoordinator? _support;
    private ConfigurationProcessLock? _settingsLease;
    private bool _exiting;
    private bool _closing;
    private bool _closeAllowed;
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

        // Closing the main window exits the whole app; otherwise the underline overlay, being a separate window, would linger (the default OnLastWindowClose never waits for it to close).
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var services = new ServiceCollection();
        ConfigureServices(services);
        services.AddSingleton(sp => new OnboardingViewModel(sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IStringLocalizer>(), () => ReadReadiness(sp),
            ct => PrepareRuntimeAsync(sp, ct),
            () => _mainViewModel!.Recognition!.StartRecognitionAsync(),
            () => { _mainViewModel?.Selection?.RefreshTarget(); }));
        _services = services.BuildServiceProvider();

        // Before any localization resource is accessed, resolve and set the UI culture from preference/OS.
        var languageService = _services.GetRequiredService<LanguageService>();
        languageService.Initialize();

        // Provides the static bridge for the XAML LocExtension to resolve keys.
        LocalizationHost.Localizer = _services.GetRequiredService<IStringLocalizer>();

        // Logging: clean up expired logs at startup, keep the logger for the global fallback (at runtime cleanup is triggered by the cross-midnight rollover event; no background timer).
        _services.GetRequiredService<LogRetentionPolicy>().Cleanup();
        _logger = _services.GetRequiredService<FileLogger>();
        _logger.LogInformation("App starting (v{0})", Environment.Version);

        ConfigureGlobalErrorHandling();

        _mainViewModel = _services.GetRequiredService<MainWindowViewModel>();
        _mainViewModel.Settings.Llm.PropertyChanged += OnReadinessChanged;
        _mainViewModel.Dictionary.PropertyChanged += OnReadinessChanged;
        _mainViewModel.Settings.OverlayAppearance.Changed += OnOverlayAppearanceChanged;
        var window = new MainWindow
        {
            ThemeService = _services.GetRequiredService<FluentThemeService>(),
            DataContext = _mainViewModel,
        };
        MainWindow = window;
        _support = new SupportCoordinator(_mainViewModel.Support!, _mainViewModel, window,
            _services.GetRequiredService<IStringLocalizer>(), _logger);
        window.Closing += OnMainWindowClosing;
        window.Show();
        _ = _mainViewModel.Onboarding.InitializeCommand.ExecuteAsync(null);
    }

    private OnboardingReadiness ReadReadiness(IServiceProvider services)
    {
        var llm = services.GetRequiredService<LlmSettingsViewModel>();
        var configuration = services.GetRequiredService<LlmConfigurationService>().GetStatus();
        var target = _mainViewModel?.Selection;
        return new(services.GetRequiredService<UniDicInstallController>().CheckInstalled(),
            llm.IsConfigurationUsable && configuration.Verified
                && !string.IsNullOrWhiteSpace(llm.Endpoint) && !string.IsNullOrWhiteSpace(llm.ApiKey)
                && !string.IsNullOrWhiteSpace(llm.Model),
            target?.SelectedWindow is not null, target?.IsTargetReady == true,
            target?.IsSelectingWindow == true, llm.IsCheckingConfiguration || llm.IsFetchingModels);
    }

    private async Task PrepareRuntimeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            var dictionary = services.GetRequiredService<UniDicInstallController>();
            await dictionary.InstallAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!dictionary.IsInstalled)
                throw new InvalidOperationException("Dictionary preparation failed.", dictionary.Failure);
            if (_mainViewModel?.Recognition is not null) return;

            // Tracking and capture bind their dispatcher during construction, before native model loading moves off-thread.
            services.GetRequiredService<ITargetWindowTracker>();
            services.GetRequiredService<IWindowFrameCapture>();
            // These constructors load large native models; WPF adapters are assembled back on the UI thread.
            await Task.Run(() =>
            {
                services.GetRequiredService<IWindowWordRecognizer>();
                services.GetRequiredService<ITokenizer>();
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_exiting || _mainViewModel is null) return;
            _mainViewModel.Recognition = services.GetRequiredService<RecognitionPageViewModel>();
            _mainViewModel.Selection!.PropertyChanged += OnReadinessChanged;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to prepare recognition services.");
            throw;
        }
    }

    private void OnReadinessChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_exiting) _mainViewModel?.Onboarding.Refresh();
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed || _mainViewModel is null) return;
        e.Cancel = true;
        if (_closing || sender is not Window window) return;
        _closing = true;
        _support?.Dispose();
        window.IsEnabled = false;
        try { await _mainViewModel.StopAsync(); }
        catch (Exception ex) { _logger?.LogError(ex, "Failed to finish cancellation during shutdown."); }
        _closeAllowed = true;
        _ = window.Dispatcher.BeginInvoke(() => window.Close());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _exiting = true;
            _support?.Dispose();
            if (_mainViewModel is not null)
            {
                _mainViewModel.Settings.OverlayAppearance.Changed -= OnOverlayAppearanceChanged;
                _mainViewModel.Settings.Llm.PropertyChanged -= OnReadinessChanged;
                _mainViewModel.Dictionary.PropertyChanged -= OnReadinessChanged;
                if (_mainViewModel.Selection is not null)
                    _mainViewModel.Selection.PropertyChanged -= OnReadinessChanged;
            }
            _mainViewModel?.Dispose();
            _services?.Dispose();
        }
        finally
        {
            _logger?.Dispose();
            _settingsLease?.Dispose();
            base.OnExit(e);
        }
    }

    private void OnOverlayAppearanceChanged(object? sender, EventArgs e)
    {
        if (_mainViewModel?.Recognition is not null)
            _services?.GetRequiredService<WpfOverlayRenderer>().RefreshAppearance();
    }

    /// <summary>HttpClient for the dictionary download: a 502MB archive download needs a long timeout to avoid failing mid-way on the default 100s.</summary>
    private static HttpClient CreateDownloadHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KotobaSenpai");
        return client;
    }

    /// <summary>Registers domain ports and their platform adapters by port; the localization ports and language service have their dependencies resolved by the container.</summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWindowPicker, Win32WindowPicker>();
        services.AddSingleton<IWindowFrameCapture, WgcWindowFrameCapture>();
        services.AddSingleton<IWindowWordRecognizer, MeikiOcrWordRecognizer>();
        services.AddSingleton<ITargetWindowTracker, WinEventTargetWindowTracker>();
        services.AddSingleton<IAutoRecognitionController, AutoRecognitionController>();
        services.AddSingleton<WpfOverlayRenderer>();
        services.AddSingleton<IOverlayRenderer>(sp => sp.GetRequiredService<WpfOverlayRenderer>());
        services.AddSingleton<IRegionSelector, RegionSelectorWindow>();

        // Phrase analysis: a protocol-agnostic adapter + pluggable protocol. Local words/spans complete first, and a failure only produces a retryable warning,
        // Cloud analysis is required; incomplete provider configuration returns NoKey with a retryable warning.
        // The protocol is selected by LlmProtocol for each request (anthropic/openai-responses/default OpenAI Chat Completions).
        services.AddSingleton<PhrasePromptBuilder>();
        services.AddSingleton<PhraseResponseParser>();
        services.AddSingleton(_ => LlmHttpClient.Create());
        services.AddSingleton<LlmProtocolRegistry>();
        services.AddSingleton<ILlmModelCatalog, HttpLlmModelCatalog>();
        services.AddSingleton<ILlmPhraseAnalyzer, LlmPhraseAnalyzer>();
        services.AddSingleton<LlmPhraseAnalyzer>();
        services.AddSingleton<ILlmConnectionDetector, LlmConnectionDetector>();
        services.AddSingleton<LlmConfigurationService>();
        services.AddSingleton<SentenceSegmenter>();
        services.AddSingleton<SentenceTokenBuilder>();
        services.AddSingleton<PhraseAnalysisOrchestrator>();

        services.AddSingleton<WordOverlayApplicationService>();
        // Diagnostics: when DiagEnabled=true, write the tokenization results to disk for offline analysis.
        services.AddSingleton<IDiagnosticReporter, FileDiagnosticReporter>();

        // Local Japanese-English dictionary: the JMdict .db is bundled with the release in the program directory; the same lookup instance provides both single and batch ports,
        // so one recognition runs a single round of candidate lookups and shares its cache.
        var jmdictDbPath = Path.Combine(AppContext.BaseDirectory, "jmdict.db");
        services.AddSingleton<IJmdictRepository>(new JmdictSqliteRepository(jmdictDbPath));
        services.AddSingleton<JmdictLookupService>();
        services.AddSingleton<IDictionaryLookup>(sp => sp.GetRequiredService<JmdictLookupService>());
        services.AddSingleton<IBatchDictionaryLookup>(sp => sp.GetRequiredService<JmdictLookupService>());
        services.AddSingleton<ITokenSpanResolver, TokenBoundarySpanResolver>();

        // Japanese tokenization: the adapter is explicitly initialized during startup after the dictionary installer completes.
        services.AddSingleton<UniDicTokenizer>();
        services.AddSingleton<ITokenizer>(sp => sp.GetRequiredService<UniDicTokenizer>());
        services.AddSingleton(sp => new UniDicDictionaryInstaller(CreateDownloadHttpClient()));
        // Install coordinator: drives the startup overlay (progress/error/retry).
        services.AddSingleton<UniDicInstallController>();
        // Word grouping: first resolve token-boundary spans in a single batched OCR pass, then reuse the same result for both underlines and hover.
        services.AddSingleton<IOcrWordGroupingService>(sp => new WordGroupingService(
            sp.GetRequiredService<ITokenizer>(),
            sp.GetRequiredService<SentenceSegmenter>(),
            sp.GetRequiredService<ITokenSpanResolver>()));

        // Settings: unified settings.json read/write, the single owner for preference storage and log-level configuration (singleton, holds the in-memory view).
        services.AddSingleton<ISettingsService>(_ => new SettingsService());

        // Localization: the concrete implementation and the Core port point at the same singleton, so LanguageService and the ViewModel share culture state.
        services.AddSingleton<ResourceManagerStringLocalizer>();
        services.AddSingleton<IStringLocalizer>(sp => sp.GetRequiredService<ResourceManagerStringLocalizer>());
        services.AddSingleton<IUserMessageResolver, UserMessageResolver>();
        services.AddSingleton<ILanguagePreferenceStore, LocalAppDataLanguagePreferenceStore>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<ILanguagePreferences>(sp => sp.GetRequiredService<LanguageService>());
        services.AddSingleton<IExplanationLanguagePreferences>(sp => sp.GetRequiredService<LanguageService>());

        // Logging: the FileLogger singleton holds the file handle; the LogRetentionPolicy singleton is shared by startup/timer/cross-midnight rollover.
        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "logs");
        services.AddSingleton(_ => new LogRetentionPolicy(logsDirectory));
        services.AddSingleton(sp => new FileLogger(
            logsDirectory,
            LogConfiguration.LoadMinimumLevel(sp.GetRequiredService<ISettingsService>()),
            sp.GetRequiredService<LogRetentionPolicy>()));
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<FileLogger>());

        // Theme: the preference-store port and the view-layer FluentThemeService singleton (holds the window OS-follow subscription; must be shared).
        services.AddSingleton<IThemePreferenceStore, LocalAppDataThemePreferenceStore>();
        services.AddSingleton<FluentThemeService>();

        services.AddSingleton<LlmSettingsViewModel>();
        services.AddSingleton<OverlayAppearanceViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton(sp => new TargetSelectionViewModel(sp.GetRequiredService<IWindowPicker>(),
            sp.GetRequiredService<IStringLocalizer>(), sp.GetRequiredService<IUserMessageResolver>(),
            sp.GetRequiredService<ILogger>(), Controls.WindowPreviewFlight.CreateThumbnailFrame,
            sp.GetRequiredService<ITargetWindowTracker>(), sp.GetRequiredService<IWindowFrameCapture>()));
        services.AddSingleton<RecognitionPageViewModel>();
        services.AddSingleton<SupportService>();
        services.AddSingleton<MainWindowViewModel>();
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
        var localizer = _services?.GetService<IStringLocalizer>();
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
