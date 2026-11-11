using KotobaSenpai.App.Guidance;
using KotobaSenpai.App.Guidance.Presentation;
using KotobaSenpai.App.Support;
using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Diagnostics;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Session;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.TargetWindow;
using System.IO;
using System.Windows;
using KotobaSenpai.App.Diagnostics;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
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
using KotobaSenpai.Core.Shortcuts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Threading;
using KotobaSenpai.Presentation.Wpf.Threading;
using System.Windows.Threading;
using KotobaSenpai.Platform.Windows.Shortcuts;
using KotobaSenpai.App.Shortcuts;
using KotobaSenpai.App.Reading;
using KotobaSenpai.Presentation.Wpf.Overlay;
using KotobaSenpai.Presentation.Wpf.Capture;
using KotobaSenpai.App.Startup;
using KotobaSenpai.Core.Updates;
using KotobaSenpai.Platform.Windows.Updates;
using System.Net.Http;

namespace KotobaSenpai.App.Composition;

/// <summary>The only container boundary: owns application resources and the main window scope.</summary>
internal sealed class DesktopComposition : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _windowScope;
    private readonly ResourceDictionary _resources;
    private readonly LocalizationHost _localization;
    private DesktopSession? _session;
    private bool _disposed;

    private DesktopComposition(ServiceProvider provider, IServiceScope windowScope, ResourceDictionary resources,
        LocalizationHost localization, ILogger logger)
    {
        _provider = provider;
        _windowScope = windowScope;
        _resources = resources;
        _localization = localization;
        Logger = logger;
    }

    public ILogger Logger { get; }
    public IStringLocalizer Localizer => _localization.Localizer;

    public static DesktopComposition Create(ResourceDictionary resources)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        IServiceScope? scope = null;
        LocalizationHost? localization = null;
        try
        {
            // Language and XAML resources are initialized before constructing any view or page state.
            provider.GetRequiredService<LanguageService>().Initialize();
            localization = provider.GetRequiredService<LocalizationHost>();
            resources[LocalizationHost.ResourceKey] = localization;
            provider.GetRequiredService<LogRetentionPolicy>().Cleanup();
            var logger = provider.GetRequiredService<ILogger>();
            scope = provider.CreateScope();
            return new DesktopComposition(provider, scope, resources, localization, logger);
        }
        catch
        {
            if (ReferenceEquals(resources[LocalizationHost.ResourceKey], localization)) resources.Remove(LocalizationHost.ResourceKey);
            scope?.Dispose();
            provider.Dispose();
            throw;
        }
    }

    public void Start(Application application)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The process installs global error handling before shell constructors run.
        _session = _windowScope.ServiceProvider.GetRequiredService<DesktopSession>();
        _session.Start(application);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session?.Dispose(); }
        finally
        {
            try { _windowScope.Dispose(); }
            finally
            {
                if (ReferenceEquals(_resources[LocalizationHost.ResourceKey], _localization))
                    _resources.Remove(LocalizationHost.ResourceKey);
                _provider.Dispose();
            }
        }
    }

    private static async Task<RecognitionRuntime> CreateRecognitionRuntimeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // Capture and tracking bind their dispatcher before model loading moves off the UI thread.
        services.GetRequiredService<ITargetWindowTracker>();
        services.GetRequiredService<IWindowFrameCapture>();
        await Task.Run(() =>
        {
            services.GetRequiredService<IWindowWordRecognizer>();
            services.GetRequiredService<ITokenizer>();
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new RecognitionRuntime(services.GetRequiredService<RecognitionPageViewModel>(), services.GetRequiredService<WpfOverlayRenderer>());
    }

    /// <summary>Registers application-wide resources and window-scoped presentation and recognition state.</summary>
    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUiThread>(_ => new WpfUiThread(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher));
        services.AddScoped<SessionEpoch>();
        services.AddScoped<IWindowPicker, Win32WindowPicker>();
        services.AddScoped<IWindowFrameCapture, WgcWindowFrameCapture>();
        services.AddScoped<MeikiOcrWordRecognizer>();
        services.AddScoped<IWindowWordRecognizer>(sp => new StableWindowWordRecognizer(
            sp.GetRequiredService<MeikiOcrWordRecognizer>(), logger: sp.GetRequiredService<ILogger>()));
        services.AddScoped<ITargetWindowTracker, WinEventTargetWindowTracker>();
        services.AddScoped<IAutoRecognitionController, AutoRecognitionController>();
        services.AddScoped<WpfOverlayRenderer>();
        services.AddScoped<IOverlayRenderer>(sp => sp.GetRequiredService<WpfOverlayRenderer>());
        services.AddScoped<ContextualGuidance>();
        services.AddScoped<IRegionSelector>(sp => new RegionSelectorWindow(sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IStringLocalizer>(), sp.GetRequiredService<ITargetWindowTracker>(),
            new TourPresentation(sp.GetRequiredService<ContextualGuidance>().Region, sp.GetRequiredService<IStringLocalizer>()),
            (bubble, button) => GuidanceLight.DecorateBubble(bubble, button, reactToPointerWhenInactive: true)));

        // Phrase analysis: a protocol-agnostic adapter + pluggable protocol. Local words/spans complete first, and a failure only produces a retryable warning,
        // Cloud analysis is required; incomplete provider configuration returns NoKey with a retryable warning.
        // LlmProtocol selects the wire format for each request; unconfigured settings default to OpenAI Responses.
        services.AddSingleton<PhrasePromptBuilder>();
        services.AddSingleton<PhraseResponseParser>();
        services.AddSingleton(_ => LlmHttpClient.Create());
        services.AddSingleton<LlmProtocolRegistry>();
        services.AddSingleton<ILlmModelCatalog, HttpLlmModelCatalog>();
        services.AddSingleton<LlmPhraseAnalyzer>();
        services.AddSingleton<ILlmPhraseAnalyzer>(sp => sp.GetRequiredService<LlmPhraseAnalyzer>());
        services.AddSingleton<ILlmConnectionDetector, LlmConnectionDetector>();
        services.AddSingleton<LlmConfigurationService>();
        services.AddSingleton<SentenceSegmenter>();
        services.AddSingleton<SentenceTokenBuilder>();
        services.AddScoped<PhraseAnalysisOrchestrator>();

        services.AddScoped<WordOverlayApplicationService>();
        services.AddScoped<RecognitionSessionController>();
        services.AddScoped<ReadingActions>();
        services.AddScoped<IReadingActions>(sp => sp.GetRequiredService<ReadingActions>());
        // Diagnostics: when DiagEnabled=true, write the tokenization results to disk for offline analysis.
        services.AddSingleton<IDiagnosticReporter, FileDiagnosticReporter>();

        // Local Japanese-English dictionary: the JMdict .db is bundled with the release in the program directory; the same lookup instance provides both single and batch ports,
        // so one recognition runs a single round of candidate lookups and shares its cache.
        var jmdictDbPath = Path.Combine(AppContext.BaseDirectory, "jmdict.db");
        services.AddSingleton<IJmdictRepository>(_ => new JmdictSqliteRepository(jmdictDbPath));
        services.AddSingleton<JmdictLookupService>();
        services.AddSingleton<IDictionaryLookup>(sp => sp.GetRequiredService<JmdictLookupService>());
        services.AddSingleton<IBatchDictionaryLookup>(sp => sp.GetRequiredService<JmdictLookupService>());
        services.AddSingleton<ITokenSpanResolver, TokenBoundarySpanResolver>();

        // Japanese tokenization: the adapter is explicitly initialized during startup after the dictionary installer completes.
        services.AddSingleton<UniDicTokenizer>();
        services.AddSingleton<ITokenizer>(sp => sp.GetRequiredService<UniDicTokenizer>());
        services.AddSingleton<IUniDicDictionaryInstaller>(_ => new UniDicDictionaryInstaller());
        // Install coordinator: drives the startup overlay (progress/error/retry).
        services.AddScoped<UniDicInstallController>();
        // Word grouping: first resolve token-boundary spans in a single batched OCR pass, then reuse the same result for both underlines and hover.
        services.AddSingleton<IOcrWordGroupingService>(sp => new WordGroupingService(
            sp.GetRequiredService<ITokenizer>(),
            sp.GetRequiredService<SentenceSegmenter>(),
            sp.GetRequiredService<ITokenSpanResolver>()));

        // Settings: unified settings.json read/write, the single owner for preference storage and log-level configuration (singleton, holds the in-memory view).
        services.AddSingleton<ISettingsService>(_ => new SettingsService());
        services.AddScoped<IGlobalShortcutRegistrar, Win32GlobalShortcutRegistrar>();
        services.AddScoped<ReadingShortcutCoordinator>();
        services.AddScoped<ShortcutSettingsViewModel>();
        services.AddSingleton<CaptureBorderPreferences>();
        services.AddSingleton<SentenceTranslationPreferences>();

        // Localization: the concrete implementation and the Core port point at the same singleton, so LanguageService and the ViewModel share culture state.
        services.AddSingleton<LocalizationHost>();
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
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new FileLogger(logsDirectory, () => LogConfiguration.LoadMinimumLevel(settings),
                sp.GetRequiredService<LogRetentionPolicy>());
        });
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<FileLogger>());

        // Theme preferences are application-wide; the theme watcher belongs to its window scope.
        services.AddSingleton<IThemePreferenceStore, LocalAppDataThemePreferenceStore>();
        services.AddScoped<FluentThemeService>();

        services.AddScoped<LlmSettingsViewModel>();
        services.AddScoped<OverlayAppearanceViewModel>();
        services.AddScoped<SettingsPageViewModel>();
        services.AddScoped<IAppUpdateSource>(_ => new GitHubAppUpdateSource(new HttpClient(), Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotobaSenpai", "updates")));
        services.AddScoped<IAppUpdateInstaller>(_ => new InnoSetupUpdateInstaller(AppContext.BaseDirectory));
        services.AddScoped(sp => new AboutPageViewModel(sp.GetRequiredService<IAppUpdateSource>(),
            sp.GetRequiredService<IAppUpdateInstaller>(), sp.GetRequiredService<IStringLocalizer>(),
            sp.GetRequiredService<IUserMessageResolver>(), sp.GetRequiredService<ILogger>(), typeof(App).Assembly.GetName().Version!));
        services.AddScoped(sp => new TargetSelectionViewModel(sp.GetRequiredService<IWindowPicker>(),
            sp.GetRequiredService<IStringLocalizer>(), sp.GetRequiredService<IUserMessageResolver>(),
            sp.GetRequiredService<ILogger>(), Controls.WindowPreviewFlight.CreateThumbnailFrame,
            sp.GetRequiredService<ITargetWindowTracker>(), sp.GetRequiredService<IWindowFrameCapture>()));
        services.AddScoped<RecognitionPageViewModel>();
        services.AddScoped<SupportService>();
        services.AddScoped<MainWindowViewModel>();
        services.AddScoped(sp => new RecognitionStartup(sp.GetRequiredService<UniDicInstallController>(),
            sp.GetRequiredService<LlmSettingsViewModel>(), sp.GetRequiredService<LlmConfigurationService>(),
            ct => CreateRecognitionRuntimeAsync(sp, ct), sp.GetRequiredService<ILogger>()));
        services.AddScoped(sp =>
        {
            var startup = sp.GetRequiredService<RecognitionStartup>();
            return new OnboardingViewModel(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<IStringLocalizer>(),
                startup.ReadReadiness, startup.PrepareAsync, startup.StartRecognitionAsync, startup.RefreshTarget);
        });
        services.AddScoped<DesktopSession>();
    }
}
