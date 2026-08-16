using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Refresh_lists_windows_excluding_self()
    {
        var self = new WindowTarget((nint)1, "Self", new ScreenRect(0, 0, 100, 100));
        var other = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 100, 100));
        var (vm, _, _, _) = CreateVm(new FakeWindowCatalog(self, other));
        vm.ExcludeHandle = (nint)1;

        vm.RefreshCommand.Execute(null);

        Assert.Single(vm.Windows);
        Assert.Equal("Other", vm.Windows[0].Title);
        Assert.Equal($"{ResourceKeys.Status_WindowsFound}:1", vm.Status);
    }

    [Fact]
    public async Task Recognize_without_selection_warns_user()
    {
        var (vm, overlay, _, _) = CreateVm(new FakeWindowCatalog());

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.Status_SelectTargetFirst, vm.Status);
        Assert.Equal(0, overlay.ShowCount);
    }

    [Fact]
    public async Task Recognize_reports_count_and_shows_overlay()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var (vm, overlay, _, _) = CreateVm(new FakeWindowCatalog(target));
        vm.RefreshCommand.Execute(null);
        vm.SelectedWindow = vm.Windows[0];

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.Status_WordsRecognized}:1", vm.Status);
        Assert.Equal(1, overlay.ShowCount);
        Assert.NotNull(overlay.LastSession);
    }

    [Fact]
    public void Hide_clears_overlay_and_updates_status()
    {
        var (vm, overlay, _, _) = CreateVm(new FakeWindowCatalog());

        vm.HideCommand.Execute(null);

        Assert.Equal(ResourceKeys.Status_Hidden, vm.Status);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void Culture_changed_re_derives_status_from_current_state()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var (vm, _, localizer, _) = CreateVm(new FakeWindowCatalog(target));
        vm.SelectedWindow = target;
        Assert.Equal($"{ResourceKeys.Status_Selected}:Other", vm.Status);

        // Simulate the rendered result changing after a culture switch; the ViewModel should recompute Status accordingly.
        localizer.Suffix = "!";
        localizer.RaiseCultureChanged();

        Assert.Equal($"{ResourceKeys.Status_Selected}:Other!", vm.Status);
    }

    [Fact]
    public void Set_recognition_region_shows_selector_for_selected_window()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var (vm, _, _, _) = CreateVm(new FakeWindowCatalog(target));
        vm.RefreshCommand.Execute(null);
        vm.SelectedWindow = vm.Windows[0];
        FakeRegionSelector.ShowCount = 0;

        vm.SetRecognitionRegionCommand.Execute(null);

        Assert.Equal(1, FakeRegionSelector.ShowCount);
        Assert.Equal("Other", FakeRegionSelector.LastTarget?.Title);
    }

    [Fact]
    public void Set_recognition_region_without_window_shows_hint()
    {
        var (vm, _, _, _) = CreateVm(new FakeWindowCatalog());

        vm.SetRecognitionRegionCommand.Execute(null);

        Assert.Equal(ResourceKeys.Status_SelectTargetFirst, vm.Status);
    }

    [Fact]
    public void Refresh_failure_logs_once_and_reports_error_status()
    {
        var (vm, _, _, logger) = CreateVm(new ThrowingCatalog());
        vm.ExcludeHandle = (nint)1;

        vm.RefreshCommand.Execute(null);

        // All error paths funnel into SetError, logging once; the status is mapped via the resolver to a fallback error code.
        Assert.Equal(1, logger.ErrorCount);
        Assert.Equal(ErrorCodes.WindowEnumerationFailed, vm.Status);
    }

    private static (MainWindowViewModel ViewModel, FakeOverlay Overlay, FakeStringLocalizer Localizer, FakeLogger Logger) CreateVm(IWindowCatalog catalog)
    {
        var overlay = new FakeOverlay();
        var workflow = new WordOverlayApplicationService(
            new FakeRecognizer(),
            new WordGroupingService(new StubTokenizer()),
            overlay);
        var localizer = new FakeStringLocalizer();
        var resolver = new UserMessageResolver(localizer);
        var logger = new FakeLogger();
        return (new MainWindowViewModel(catalog, workflow, new FakeRegionSelector(), new FakeSettings(), localizer, resolver, logger), overlay, localizer, logger);
    }

    private sealed class FakeWindowCatalog : IWindowCatalog
    {
        private readonly IReadOnlyList<WindowTarget> _windows;
        public FakeWindowCatalog(params WindowTarget[] windows) => _windows = windows;
        public IReadOnlyList<WindowTarget> ListVisibleWindows() => _windows;
    }

    private sealed class ThrowingCatalog : IWindowCatalog
    {
        public IReadOnlyList<WindowTarget> ListVisibleWindows() => throw new InvalidOperationException("boom");
    }

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null) =>
            Task.FromResult(new WordRecognitionResult(100, 50,
                [new OcrLine([new OcrWord("日", new ScreenRect(10, 5, 10, 10))])]));
    }

    private sealed class FakeRegionSelector : IRegionSelector
    {
        public static WindowTarget? LastTarget;
        public static int ShowCount;
        public void Show(WindowTarget target, RecognitionRegion? initial = null) { LastTarget = target; ShowCount++; }
    }

    private sealed class FakeSettings : ISettingsService
    {
        public string? GetValue(string key) => null;
        public void SetValue(string key, string? value) { }
    }

    private sealed class StubTokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text)
            => [new Token("日", "日", "日", "日", "日", "日", new PartsOfSpeech("", "", "", ""), "", "", "", 0)];
    }

    private sealed class FakeOverlay : IOverlayRenderer
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public WordOverlaySession? LastSession { get; private set; }

        public void Show(WordOverlaySession session)
        {
            ShowCount++;
            LastSession = session;
        }

        public void Hide() => HideCount++;
    }

    /// <summary>Localization fake: returns a recognizable string per key (including Suffix so culture-switch re-rendering can be tested).</summary>
    private sealed class FakeStringLocalizer : IStringLocalizer
    {
        public string Suffix { get; set; } = string.Empty;

        public event EventHandler? CultureChanged;

        public string Get(string key, params object[] args)
            => args.Length == 0 ? $"{key}{Suffix}" : $"{key}:{string.Join(",", args)}{Suffix}";

        public void RaiseCultureChanged() => CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Logging fake: captures Error-level call count and exceptions, verifying error paths log exactly once.</summary>
    private sealed class FakeLogger : ILogger
    {
        public int ErrorCount { get; private set; }
        public Exception? LastError { get; private set; }

        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (level == LogLevel.Error)
            {
                ErrorCount++;
                LastError = exception;
            }
        }
    }
}
