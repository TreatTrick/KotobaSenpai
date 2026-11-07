using KotobaSenpai.Core.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.AutoRecognition;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class AutoRecognitionControllerTests
{
    [Fact]
    public void Timer_posts_are_coalesced_and_stopped_generations_cannot_sample_a_restarted_run()
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 2, 1));
        var capture = new SequencedCapture(new CapturedFrame(2, 1, new byte[8]));
        var thread = new InlineUiThread();
        using var controller = new AutoRecognitionController(capture, new FakeTracker(target), new FakeSettings(), thread);
        controller.Start(target);
        controller.SchedulePoll();
        controller.SchedulePoll();
        Assert.Equal(1, thread.PendingCount);

        controller.Stop();
        controller.Start(target);
        thread.RunPosted();
        Assert.Equal(0, capture.CallCount);

        controller.SchedulePoll();
        thread.RunPosted();
        Assert.Equal(1, capture.CallCount);
        controller.Dispose();
        controller.Stop();
        controller.SchedulePoll();
        Assert.Equal(0, thread.PendingCount);
    }

    [Fact]
    public async Task Small_typewriter_changes_accumulate_against_the_last_OCR_trigger()
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 200, 1));
        var oneCharacter = new byte[800];
        oneCharacter[0] = 255;
        var twoCharacters = (byte[])oneCharacter.Clone();
        twoCharacters[4] = 255;
        var capture = new SequencedCapture(new CapturedFrame(200, 1, new byte[800]),
            new CapturedFrame(200, 1, oneCharacter), new CapturedFrame(200, 1, twoCharacters));
        using var controller = new AutoRecognitionController(capture, new FakeTracker(target), new FakeSettings(), new InlineUiThread());
        var changes = 0;
        controller.Start(target, _ => { changes++; return Task.CompletedTask; });

        await controller.PollOnceAsync();
        await controller.PollOnceAsync();
        Assert.Equal(0, changes);
        await controller.PollOnceAsync();
        await controller.PollOnceAsync();

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Stopping_during_capture_cancels_the_run_and_discards_its_late_sample()
    {
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 2, 1));
        var capture = new DeferredCapture();
        var controller = new AutoRecognitionController(capture, new FakeTracker(target), new FakeSettings(), new InlineUiThread());
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
        using var controller = new AutoRecognitionController(capture, tracker, settings, new InlineUiThread());
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
        using var controller = new AutoRecognitionController(capture, tracker, new FakeSettings(), new InlineUiThread());
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
            uiThread: new InlineUiThread());
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

    private sealed class FakeSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        public string? Region { get; set; }
        public override string? GetValue(string key) => key == RecognitionRegion.SettingsKey ? Region : null;

        public override void SetValue(string key, string? value) { }
    }
}
