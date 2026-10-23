using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.App.Tests;

public sealed class AutoRecognitionSettingsTests
{
    [Fact]
    public void Auto_recognition_defaults_to_disabled_when_the_setting_is_missing()
    {
        var settings = new RecordingSettings();
        var viewModel = Create(settings);

        Assert.False(viewModel.AutoRecognitionEnabled);
        Assert.Null(settings.GetValue(AutoRecognitionSettings.EnabledKey));
    }

    [Fact]
    public void Changing_auto_recognition_persists_the_setting()
    {
        var settings = new RecordingSettings();
        var viewModel = Create(settings);

        viewModel.AutoRecognitionEnabled = true;

        Assert.Equal("true", settings.GetValue(AutoRecognitionSettings.EnabledKey));
    }

    [Fact]
    public async Task Enabled_auto_recognition_starts_after_manual_recognition_and_stops_when_disabled()
    {
        var settings = new RecordingSettings();
        var controller = new FakeAutoRecognitionController();
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var viewModel = Create(settings, controller, target);

        viewModel.RefreshCommand.Execute(null);
        viewModel.SelectedWindow = viewModel.Windows[0];
        viewModel.AutoRecognitionEnabled = true;

        await viewModel.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal(1, controller.StartCount);
        Assert.True(controller.IsRunning);

        viewModel.AutoRecognitionEnabled = false;

        Assert.False(controller.IsRunning);
        Assert.Equal("false", settings.GetValue(AutoRecognitionSettings.EnabledKey));
    }

    private sealed class RecordingSettings : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = new();

        public string? GetValue(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void SetValue(string key, string? value) => _values[key] = value;
    }

    private static RecognitionPageViewModel Create(
        ISettingsService settings,
        IAutoRecognitionController? controller = null,
        WindowTarget? target = null)
    {
        var localizer = new FakeLocalizer();
        var workflow = new WordOverlayApplicationService(
            new FakeRecognizer(),
            new WordGroupingService(new FakeTokenizer(), new SentenceSegmenter(), new FakeSpanResolver()),
            new FakeOverlay());
        return new RecognitionPageViewModel(
            new FakeWindowCatalog(target),
            workflow,
            new FakeRegionSelector(),
            settings,
            localizer,
            new UserMessageResolver(localizer),
            new FakeLogger(),
            autoRecognitionController: controller);
    }

    private sealed class FakeWindowCatalog(WindowTarget? target) : IWindowCatalog
    {
        public IReadOnlyList<WindowTarget> ListVisibleWindows() => target is null ? [] : [target];
    }

    private sealed class FakeRegionSelector : IRegionSelector
    {
        public void Show(WindowTarget target, RecognitionRegion? initial = null) { }
    }

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(
            Guid recognitionId,
            WindowTarget target,
            CancellationToken cancellationToken = default,
            ScreenRect? region = null)
            => Task.FromResult(new WordRecognitionResult(1, 1, []));
    }

    private sealed class FakeOverlay : IOverlayRenderer
    {
        public void Show(WordOverlaySession session, Func<bool>? canRender = null) { }

        public void Hide() { }
    }

    private sealed class FakeTokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text) => [];
    }

    private sealed class FakeSpanResolver : ITokenSpanResolver
    {
        public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens) => [];
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
        private EventHandler? _cultureChanged;

        public event EventHandler? CultureChanged
        {
            add => _cultureChanged += value;
            remove => _cultureChanged -= value;
        }

        public string Get(string key, params object[] args) => key;
    }

    private sealed class FakeLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    }

    private sealed class FakeAutoRecognitionController : IAutoRecognitionController
    {
        private bool _running;

        public int StartCount { get; private set; }

        public bool IsRunning => _running;

        public void Start(WindowTarget target, Func<CancellationToken, Task>? onChange = null)
        {
            StartCount++;
            _running = true;
        }

        public void Stop() => _running = false;

        public void Dispose() => _running = false;
    }
}
