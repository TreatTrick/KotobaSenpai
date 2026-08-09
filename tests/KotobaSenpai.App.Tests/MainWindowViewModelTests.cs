using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

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

        // 模拟文化切换后渲染结果改变；ViewModel 应据此重算 Status。
        localizer.Suffix = "!";
        localizer.RaiseCultureChanged();

        Assert.Equal($"{ResourceKeys.Status_Selected}:Other!", vm.Status);
    }

    [Fact]
    public void Refresh_failure_logs_once_and_reports_error_status()
    {
        var (vm, _, _, logger) = CreateVm(new ThrowingCatalog());
        vm.ExcludeHandle = (nint)1;

        vm.RefreshCommand.Execute(null);

        // 所有错误路径汇集到 SetError，记一次日志；状态经解析器映射为回退错误码。
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
        return (new MainWindowViewModel(catalog, workflow, localizer, resolver, logger), overlay, localizer, logger);
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
        public Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordRecognitionResult(100, 50,
                [new OcrLine([new OcrWord("日", new ScreenRect(10, 5, 10, 10))])]));
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

    /// <summary>本地化 fake：按键返回可辨识字符串（含 Suffix 以便测试文化切换后的重渲染）。</summary>
    private sealed class FakeStringLocalizer : IStringLocalizer
    {
        public string Suffix { get; set; } = string.Empty;

        public event EventHandler? CultureChanged;

        public string Get(string key, params object[] args)
            => args.Length == 0 ? $"{key}{Suffix}" : $"{key}:{string.Join(",", args)}{Suffix}";

        public void RaiseCultureChanged() => CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>日志 fake：捕获 Error 级调用次数与异常，验证错误路径恰好记一次。</summary>
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
