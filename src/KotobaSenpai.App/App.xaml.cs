using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using KotobaSenpai.App.Diagnostics;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.Settings;
using KotobaSenpai.App.Themes;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Platform.Windows.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Services;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Platform.Windows.Dictionary;
using KotobaSenpai.Platform.Windows.Japanese;
using KotobaSenpai.Platform.Windows.Ocr;
using KotobaSenpai.Platform.Windows.Overlay;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Closing the main window exits the whole app; otherwise the underline overlay, being a separate window, would linger (the default OnLastWindowClose never waits for it to close).
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var services = new ServiceCollection();
        ConfigureServices(services);
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

        var themeService = _services.GetRequiredService<FluentThemeService>();
        var installController = _services.GetRequiredService<UniDicInstallController>();

        var window = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            LanguageService = languageService,
            ThemeService = themeService,
            InstallController = installController,
            SettingsService = _services.GetRequiredService<ISettingsService>()
        };
        window.Show();

        // Dictionary install: triggered after the main window is shown; the overlay blocks all other operations and shows progress/errors; on failure it can retry inside the overlay.
        if (!installController.IsInstalled)
            installController.InstallCommand.Execute(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
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
        services.AddSingleton<IWindowCatalog, Win32WindowCatalog>();
        services.AddSingleton<IWindowFrameCapture, GdiWindowFrameCapture>();
        services.AddSingleton<IWindowWordRecognizer, MeikiOcrWordRecognizer>();
        services.AddSingleton<IOverlayRenderer, WpfOverlayRenderer>();
        services.AddSingleton<IRegionSelector, RegionSelectorWindow>();

        // Phrase analysis: a protocol-agnostic adapter + pluggable protocol. Local words/spans complete first, and a failure only produces a retryable warning,
        // enabled by the config key PhraseGroupsEnabled=true; when the key is not configured the adapter returns NoKey and skips the call.
        // The protocol is selected by LlmProtocol (anthropic/responses/default OpenAI Chat Completions).
        services.AddSingleton<PhrasePromptBuilder>();
        services.AddSingleton<PhraseResponseParser>();
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<ILlmProtocol>(sp =>
        {
            var key = sp.GetRequiredService<ISettingsService>().GetValue(LlmSettingsKeys.Protocol);
            return key switch
            {
                "anthropic" => new AnthropicMessagesProtocol(),
                "responses" => new OpenAiResponsesProtocol(),
                _ => new OpenAiChatCompletionsProtocol(),
            };
        });
        services.AddSingleton<ILlmPhraseAnalyzer, LlmPhraseAnalyzer>();
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

        // Japanese tokenization: the platform adapter lazily loads the UniDic dictionary; the installer singleton holds a fixed-URL HttpClient (downloaded on first M1 run).
        services.AddSingleton<ITokenizer>(sp => new UniDicTokenizer(sp.GetRequiredService<ILogger>()));
        services.AddSingleton(sp => new UniDicDictionaryInstaller(CreateDownloadHttpClient()));
        // Install coordinator: drives the startup overlay (progress/error/retry).
        services.AddSingleton<UniDicInstallController>();
        // Word grouping: first resolve token-boundary spans in a single batched OCR pass, then reuse the same result for both underlines and hover.
        services.AddSingleton<IOcrWordGroupingService>(sp => new WordGroupingService(
            sp.GetRequiredService<ITokenizer>(),
            sp.GetRequiredService<SentenceSegmenter>(),
            sp.GetRequiredService<ITokenSpanResolver>()));

        // Settings: unified settings.json read/write, the single owner for preference storage and log-level configuration (singleton, holds the in-memory view).
        services.AddSingleton<ISettingsService, SettingsService>();

        // Localization: the concrete implementation and the Core port point at the same singleton, so LanguageService and the ViewModel share culture state.
        services.AddSingleton<ResourceManagerStringLocalizer>();
        services.AddSingleton<IStringLocalizer>(sp => sp.GetRequiredService<ResourceManagerStringLocalizer>());
        services.AddSingleton<IUserMessageResolver, UserMessageResolver>();
        services.AddSingleton<ILanguagePreferenceStore, LocalAppDataLanguagePreferenceStore>();
        services.AddSingleton<LanguageService>();

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
        // Log (FileLogger flushes on each write, so it is on disk before a crash), then prompt the user, then exit in a controlled way.
        Try(() => _logger?.LogError(e.Exception, "Unhandled exception on dispatcher thread"));
        ShowUnexpectedErrorNotice();
        e.Handled = true;
        Shutdown(1);
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
