using System.Windows.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.AutoRecognition;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class AutoRecognitionControllerTests
{
    [Fact]
    public async Task Skips_unavailable_targets_and_triggers_after_a_changed_frame()
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 2, 1));
        var tracker = new FakeTracker(target);
        var capture = new SequencedCapture(
            new CapturedFrame(2, 1, new byte[8]),
            new CapturedFrame(2, 1, new byte[] { 255, 0, 0, 255, 0, 0, 0, 255 }));
        var settings = new FakeSettings();
        var controller = new AutoRecognitionController(
            capture,
            tracker,
            settings,
            dispatcher: Dispatcher.CurrentDispatcher);
        var callbackCount = 0;
        controller.Start(target, _ =>
        {
            callbackCount++;
            return Task.CompletedTask;
        });

        tracker.IsCapturable = false;
        await controller.PollOnceAsync();
        Assert.Equal(0, capture.CallCount);

        tracker.IsCapturable = true;
        await controller.PollOnceAsync();
        await controller.PollOnceAsync();

        Assert.Equal(2, capture.CallCount);
        Assert.Equal(1, callbackCount);
        controller.Dispose();
    }

    private sealed class SequencedCapture(params CapturedFrame[] frames) : IWindowFrameCapture
    {
        private int _index;

        public int CallCount => _index;

        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
            => Task.FromResult(frames[Math.Min(_index++, frames.Length - 1)]);
    }

    private sealed class FakeTracker(WindowTarget target) : ITargetWindowTracker
    {
        private EventHandler<TargetWindowSnapshot>? _changed;

        public event EventHandler<TargetWindowSnapshot>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public bool IsCapturable { get; set; } = true;

        public TargetWindowSnapshot? Current
            => new(target.Handle, target.Title, target.Bounds, 1, IsCapturable, !IsCapturable, true);

        public TargetWindowSnapshot Attach(WindowTarget value) => Current!;

        public TargetWindowSnapshot Refresh() => Current!;

        public void Detach() { }

        public void Dispose() { }
    }

    private sealed class FakeSettings : ISettingsService
    {
        public string? GetValue(string key) => null;

        public void SetValue(string key, string? value) { }
    }
}
