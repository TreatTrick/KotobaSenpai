using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Guidance;
using KotobaSenpai.App.Onboarding;
using KotobaSenpai.App.Shortcuts;
using KotobaSenpai.App.Support;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Shortcuts;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Threading;

namespace KotobaSenpai.App.Tests;

/// <summary>Explicit test composition for page dependencies that are required in production.</summary>
internal static class ViewModelDependencies
{
    public static ILogger Logger { get; } = new SilentLogger();
    public static IStringLocalizer Localizer() => new ResourceManagerStringLocalizer();
    public static ISettingsService Settings() => new MemorySettings();
    public static SupportService Support() => new(Settings(), Localizer(), Logger);
    public static ContextualGuidance Guidance() => new(Settings(), Logger);
    public static ShortcutSettingsViewModel Shortcuts()
        => new(new ReadingShortcutCoordinator(Settings(), new Registrar()), Localizer());
    public static LlmConfigurationService Configuration(ISettingsService settings, ILlmModelCatalog catalog)
        => new(settings, new Detector(), catalog);
    public static LlmSettingsViewModel Llm(ISettingsService settings, ILlmModelCatalog catalog, IStringLocalizer localizer)
        => new(settings, catalog, localizer, Configuration(settings, catalog));
    public static OnboardingViewModel Onboarding(ISettingsService settings, IStringLocalizer localizer,
        Func<OnboardingReadiness> readiness, Func<CancellationToken, Task> prepare, Func<Task> start)
        => new(settings, localizer, readiness, prepare, start, static () => { });
    public static ITargetWindowTracker Tracker() => new TrackerStub();
    public static IWindowFrameCapture Capture() => new CaptureStub();
    public static IUiThread UiThread() => new InlineUiThread();

    private sealed class InlineUiThread : IUiThread
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public T Invoke<T>(Func<T> action) => action();
    }

    private sealed class MemorySettings : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => null;
        public override void SetValue(string key, string? value) { }
    }
    private sealed class SilentLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    }
    private sealed class Detector : ILlmConnectionDetector
    {
        public Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmDetectionResult(candidate));
        public Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmDetectionResult(configuration));
    }
    private sealed class Registrar : IGlobalShortcutRegistrar
    {
        public event EventHandler<int>? Triggered { add { } remove { } }
        public bool TryRegister(int registrationId, ShortcutGesture gesture, out int error) { error = 0; return true; }
        public void Unregister(int registrationId) { }
        public void Dispose() { }
    }
    private sealed class TrackerStub : ITargetWindowTracker
    {
        public event EventHandler<TargetWindowSnapshot>? Changed { add { } remove { } }
        public TargetWindowSnapshot? Current { get; private set; }
        public TargetWindowSnapshot Attach(WindowTarget target)
            => Current = new(target.Handle, target.Title, target.Bounds, 1, true, false, true);
        public TargetWindowSnapshot Refresh() => Current ?? throw new InvalidOperationException("No target is selected.");
        public void Detach() => Current = null;
        public void Dispose() { }
    }
    private sealed class CaptureStub : IWindowFrameCapture
    {
        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
            => Task.FromResult(new CapturedFrame(target.Bounds.Width, target.Bounds.Height,
                new byte[checked(target.Bounds.Width * target.Bounds.Height * 4)]));
        public void Stop() { }
    }
}
