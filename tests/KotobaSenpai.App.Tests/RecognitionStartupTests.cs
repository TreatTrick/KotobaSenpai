using System.Windows.Threading;
using KotobaSenpai.App.Composition;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Startup;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App.Tests;

public sealed class RecognitionStartupTests
{
    [Fact]
    public Task Lazy_runtime_binds_window_dependencies_before_loading_models_and_publishes_once() => RunDispatcher(async () =>
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var trackerThread = 0;
        var captureThread = 0;
        var modelThread = 0;
        var loads = 0;
        var services = CreateServices();
        services.AddScoped<ITargetWindowTracker>(_ => { trackerThread = Environment.CurrentManagedThreadId; return new Tracker(); });
        services.AddScoped<IWindowFrameCapture>(_ => { captureThread = Environment.CurrentManagedThreadId; return new Capture(); });
        services.AddScoped<IWindowWordRecognizer>(_ =>
        {
            loads++;
            modelThread = Environment.CurrentManagedThreadId;
            return new Recognizer();
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var startup = scope.ServiceProvider.GetRequiredService<RecognitionStartup>();
        var publications = 0;
        startup.Prepared += (_, _) =>
        {
            Assert.Equal(callerThread, Environment.CurrentManagedThreadId);
            publications++;
        };
        Assert.Equal(0, loads);

        await startup.PrepareAsync(CancellationToken.None);
        await startup.PrepareAsync(CancellationToken.None);

        Assert.Equal(callerThread, trackerThread);
        Assert.Equal(callerThread, captureThread);
        Assert.NotEqual(callerThread, modelThread);
        Assert.Equal(1, loads);
        Assert.Equal(1, publications);
        Assert.Same(scope.ServiceProvider.GetRequiredService<RecognitionPageViewModel>(), startup.Current!.Page);
        Assert.Same(scope.ServiceProvider.GetRequiredService<KotobaSenpai.App.Reading.ReadingActions>(), startup.Current.Page.Actions);
    });

    [Fact]
    public Task Shutdown_waits_for_background_model_loading_and_does_not_publish_a_cancelled_runtime() => RunDispatcher(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var services = CreateServices();
        services.AddScoped<IWindowWordRecognizer>(_ => { entered.SetResult(); release.Wait(); return new Recognizer(); });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var main = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
        var startup = scope.ServiceProvider.GetRequiredService<RecognitionStartup>();
        var publications = 0;
        startup.Prepared += (_, _) => publications++;
        var initializing = main.Onboarding.InitializeCommand.ExecuteAsync(null);
        await entered.Task;

        var stopping = main.StopAsync();
        var stoppedBeforeLoadingFinished = stopping.IsCompleted;
        release.Set();
        await stopping;
        await initializing;

        Assert.False(stoppedBeforeLoadingFinished);
        Assert.Equal(0, publications);
        Assert.Null(startup.Current);
        Assert.Null(main.Recognition);
    });

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        DesktopComposition.ConfigureServices(services);
        services.AddSingleton<ISettingsService>(new Settings());
        services.AddSingleton<IUniDicDictionaryInstaller>(new DictionaryInstallerStub());
        services.AddScoped<ITargetWindowTracker>(_ => new Tracker());
        services.AddScoped<IWindowFrameCapture>(_ => new Capture());
        services.AddScoped<IRegionSelector>(_ => new Region());
        services.AddScoped<IAutoRecognitionController>(_ => new Automatic());
        services.AddScoped<IOverlayRenderer>(_ => new Overlay());
        services.AddSingleton<ITokenizer>(new Tokenizer());
        return services;
    }

    private sealed class Settings : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => null;
        public override void SetValue(string key, string? value) { }
    }
    private sealed class Recognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
            => throw new InvalidOperationException("Runtime preparation must not perform recognition.");
    }
    private sealed class Tokenizer : ITokenizer { public IReadOnlyList<Token> Tokenize(string? text) => []; }
    private sealed class Tracker : ITargetWindowTracker
    {
        public event EventHandler<TargetWindowSnapshot>? Changed { add { } remove { } }
        public TargetWindowSnapshot? Current => null;
        public TargetWindowSnapshot Attach(WindowTarget target) => throw new InvalidOperationException("No target is selected.");
        public TargetWindowSnapshot Refresh() => throw new InvalidOperationException("No target is selected.");
        public void Detach() { }
        public void Dispose() { }
    }
    private sealed class Capture : IWindowFrameCapture
    {
        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
            => throw new InvalidOperationException("Runtime preparation must not capture frames.");
        public void Stop() { }
    }
    private sealed class Region : IRegionSelector
    {
        public bool IsSelecting => false;
        public event EventHandler? SelectionStateChanged { add { } remove { } }
        public void Show(WindowTarget target, RecognitionRegion? initial = null) { }
        public void Flash(WindowTarget target, RecognitionRegion? region = null) { }
        public void StopFlashing() { }
        public void Cancel() { }
    }
    private sealed class Automatic : IAutoRecognitionController
    {
        public bool IsRunning => false;
        public void Start(WindowTarget target, Func<CancellationToken, Task>? onChange = null) { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Overlay : IOverlayRenderer
    {
        public void Show(WordOverlaySession session, Func<bool>? canRender = null) { }
        public void Hide() { }
        public void Suspend() { }
    }
    private static Task RunDispatcher(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
