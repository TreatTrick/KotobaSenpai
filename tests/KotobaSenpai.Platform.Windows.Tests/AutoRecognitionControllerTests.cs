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
    public async Task Stopping_during_capture_cancels_the_run_and_discards_its_late_sample()
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 2, 1));
        var capture = new DeferredCapture();
        var controller = new AutoRecognitionController(capture, new FakeTracker(target), new FakeSettings());
        var changes = 0;
        controller.Start(target, _ => { changes++; return Task.CompletedTask; });
        var pending = controller.PollOnceAsync();
        await controller.PollOnceAsync();
        Assert.Equal(1, capture.CallCount);
        controller.Stop();
        controller.Dispose();
        Assert.True(capture.CancellationToken.IsCancellationRequested);
        capture.Completion.SetResult(new CapturedFrame(2, 1, new byte[8]));
        await pending;
        Assert.False(controller.IsRunning);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("region")]
    [InlineData("size")]
    [InlineData("unavailable")]
    [InlineData("failure")]
    public async Task Incompatible_samples_rebuild_the_baseline_without_recognizing(string change)
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 2, 1));
        var tracker = new FakeTracker(target);
        var settings = new FakeSettings();
        var capture = new SequencedCapture(new CapturedFrame(2, 1, new byte[8]),
            new CapturedFrame(2, 1, Enumerable.Repeat((byte)255, 8).ToArray(), change == "generation" ? 1 : 0));
        using var controller = new AutoRecognitionController(capture, tracker, settings);
        var changes = 0;
        controller.Start(target, _ => { changes++; return Task.CompletedTask; });
        await controller.PollOnceAsync();
        if (change == "region") settings.Region = "0,0,0.5,1";
        if (change == "size") tracker.Target = new WindowTarget(target.Handle, target.Title, new ScreenRect(0, 0, 4, 1));
        if (change == "unavailable")
        {
            tracker.IsCapturable = false;
            await controller.PollOnceAsync();
            tracker.IsCapturable = true;
        }
        if (change == "failure")
        {
            capture.FailNext = true;
            await controller.PollOnceAsync();
        }
        await controller.PollOnceAsync();
        await controller.PollOnceAsync();
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Occlusion_keeps_the_baseline_and_static_frames_do_not_repeat_recognition()
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 2, 1));
        var tracker = new FakeTracker(target);
        var capture = new SequencedCapture(new CapturedFrame(2, 1, new byte[8]),
            new CapturedFrame(2, 1, Enumerable.Repeat((byte)255, 8).ToArray()));
        using var controller = new AutoRecognitionController(capture, tracker, new FakeSettings());
        var changes = 0;
        controller.Start(target, _ => { changes++; return Task.CompletedTask; });
        await controller.PollOnceAsync();
        tracker.IsOccluded = true;
        await controller.PollOnceAsync();
        await controller.PollOnceAsync();
        controller.Stop();
        await controller.PollOnceAsync();
        Assert.Equal(1, changes);
        Assert.Equal(3, capture.CallCount);
    }

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
        public void Stop() { }

        private int _index;

        public int CallCount => _index;
        public bool FailNext { get; set; }

        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Capture failed.");
            }
            return Task.FromResult(frames[Math.Min(_index++, frames.Length - 1)]);
        }
    }

    private sealed class DeferredCapture : IWindowFrameCapture
    {
        public TaskCompletionSource<CapturedFrame> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CancellationToken { get; private set; }
        public int CallCount { get; private set; }
        public void Stop() { }
        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            return Completion.Task;
        }
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
        public bool IsOccluded { get; set; }
        public WindowTarget Target { get; set; } = target;

        public TargetWindowSnapshot? Current
            => new(Target.Handle, Target.Title, Target.Bounds, 1, IsCapturable, !IsCapturable, false, IsOccluded);

        public TargetWindowSnapshot Attach(WindowTarget value) => Current!;

        public TargetWindowSnapshot Refresh() => Current!;

        public void Detach() { }

        public void Dispose() { }
    }

    private sealed class FakeSettings : ISettingsService
    {
        public string? Region { get; set; }
        public string? GetValue(string key) => key == RecognitionRegion.SettingsKey ? Region : null;

        public void SetValue(string key, string? value) { }
    }
}
