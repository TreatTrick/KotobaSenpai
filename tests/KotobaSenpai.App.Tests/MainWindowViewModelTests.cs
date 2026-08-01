using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
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
        var (vm, _) = CreateVm(new FakeWindowCatalog(self, other));
        vm.ExcludeHandle = (nint)1;

        vm.RefreshCommand.Execute(null);

        Assert.Single(vm.Windows);
        Assert.Equal("Other", vm.Windows[0].Title);
        Assert.Contains("1 个窗口", vm.Status);
    }

    [Fact]
    public async Task Recognize_without_selection_warns_user()
    {
        var (vm, overlay) = CreateVm(new FakeWindowCatalog());

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal("请先选择目标窗口。", vm.Status);
        Assert.Equal(0, overlay.ShowCount);
    }

    [Fact]
    public async Task Recognize_reports_count_and_shows_overlay()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var (vm, overlay) = CreateVm(new FakeWindowCatalog(target));
        vm.RefreshCommand.Execute(null);
        vm.SelectedWindow = vm.Windows[0];

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Contains("1 个词", vm.Status);
        Assert.Equal(1, overlay.ShowCount);
        Assert.NotNull(overlay.LastSession);
    }

    [Fact]
    public void Hide_clears_overlay_and_updates_status()
    {
        var (vm, overlay) = CreateVm(new FakeWindowCatalog());

        vm.HideCommand.Execute(null);

        Assert.Equal("下划线已隐藏。", vm.Status);
        Assert.Equal(1, overlay.HideCount);
    }

    private static (MainWindowViewModel ViewModel, FakeOverlay Overlay) CreateVm(IWindowCatalog catalog)
    {
        var overlay = new FakeOverlay();
        var workflow = new WordOverlayApplicationService(new FakeRecognizer(), overlay);
        return (new MainWindowViewModel(catalog, workflow), overlay);
    }

    private sealed class FakeWindowCatalog : IWindowCatalog
    {
        private readonly IReadOnlyList<WindowTarget> _windows;
        public FakeWindowCatalog(params WindowTarget[] windows) => _windows = windows;
        public IReadOnlyList<WindowTarget> ListVisibleWindows() => _windows;
    }

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordRecognitionResult(100, 50, [new OcrWord("日", new ScreenRect(10, 5, 10, 10))]));
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
}
