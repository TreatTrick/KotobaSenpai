using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows;

namespace KotobaSenpai.App.Tests;

public sealed class RecognitionPageViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_and_actual_automatic_completions_each_count_once_even_without_words(bool hasWords)
    {
        var settings = new SupportSettings();
        using var support = new KotobaSenpai.App.Support.SupportService(settings, new FakeStringLocalizer(), new FakeLogger());
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var result = hasWords
            ? new WordRecognitionResult(100, 100, [new OcrLine([new OcrWord("日", new ScreenRect(0, 0, 10, 10))])])
            : new WordRecognitionResult(100, 100, []);
        var controller = new RecordingAutoController();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), recognizer: new SequenceRecognizer(result, result),
            controller: controller, support: support);
        using var lifetime = vm;
        vm.Selection.SelectedWindow = target;
        vm.AutoRecognitionEnabled = true;
        await vm.StartRecognitionAsync();
        Assert.Equal(1, support.State.RecognitionCount);
        await controller.ChangeAsync();
        Assert.Equal(2, support.State.RecognitionCount);
        Assert.Single(support.State.ActiveDays);
    }

    [Fact]
    public async Task Cancelled_manual_results_do_not_count_as_support_usage()
    {
        using var support = new KotobaSenpai.App.Support.SupportService(new SupportSettings(), new FakeStringLocalizer(), new FakeLogger());
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var recognizer = new DeferredRecognizer();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), recognizer: recognizer, support: support);
        using var lifetime = vm;
        vm.Selection.SelectedWindow = target;
        var pending = vm.StartRecognitionAsync();
        vm.HideCommand.Execute(null);
        recognizer.Completion.SetResult(new WordRecognitionResult(100, 100, []));
        await pending;
        Assert.Equal(0, support.State.RecognitionCount);
    }

    private sealed class SupportSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private string? _raw;
        public override string? GetValue(string key) => _raw;
        public override void SetValue(string key, string? value) => _raw = value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_expired_automatic_results_do_not_increase_usage(bool cancelSession)
    {
        using var support = new KotobaSenpai.App.Support.SupportService(new SupportSettings(), new FakeStringLocalizer(), new FakeLogger());
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var pending = new TaskCompletionSource<WordRecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = new QueuedRecognizer(Task.FromResult(new WordRecognitionResult(100, 100, [])), pending.Task);
        var controller = new RecordingAutoController();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), recognizer: recognizer, controller: controller, support: support);
        using var lifetime = vm;
        vm.Selection.SelectedWindow = target;
        vm.AutoRecognitionEnabled = true;
        await vm.StartRecognitionAsync();
        var automatic = controller.ChangeAsync();
        if (cancelSession)
        {
            vm.HideCommand.Execute(null);
            pending.SetResult(new WordRecognitionResult(100, 100, []));
        }
        else pending.SetException(new InvalidOperationException("Automatic recognition failed."));
        await automatic;
        Assert.Equal(1, support.State.RecognitionCount);
    }

    private sealed class QueuedRecognizer(params Task<WordRecognitionResult>[] results) : IWindowWordRecognizer
    {
        private readonly Queue<Task<WordRecognitionResult>> _results = new(results);
        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target,
            CancellationToken cancellationToken = default, ScreenRect? region = null) => _results.Dequeue();
    }

    [Fact]
    public async Task Shared_selection_retains_a_small_preview_without_a_page_or_an_event_subscriber()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture);
        using var lifetime = vm;

        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Selection.PreviewFrame);
        Assert.True(vm.Selection.PreviewFrame.Width <= 288);
        Assert.Equal(1, capture.Calls);
        Assert.True(vm.Selection.IsTargetReady);
    }

    [Fact]
    public async Task Selection_stays_highlighted_through_preview_failure_and_retry_until_recognition_starts()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var picker = new FakeWindowPicker(target);
        var capture = new PreviewCapture { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var recognizer = new DeferredRecognizer();
        var (vm, _, _, _) = CreateVm(picker, capture: capture, recognizer: recognizer);
        using var lifetime = vm;

        var selecting = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        Assert.True(vm.Selection.IsSelectingWindow);
        Assert.Equal(target, picker.Highlighted);
        capture.Pending.SetException(new InvalidOperationException("Preview failed."));
        await selecting;
        Assert.True(vm.Selection.HasPreviewError);
        Assert.Equal(target, picker.Highlighted);

        capture.Pending = null;
        await vm.Selection.RetryPreviewCommand.ExecuteAsync(null);
        Assert.True(vm.Selection.HasPreview);
        Assert.Equal(target, picker.Highlighted);

        var recognizing = vm.RecognizeCommand.ExecuteAsync(null);
        Assert.Null(picker.Highlighted);
        recognizer.Completion.SetResult(new WordRecognitionResult(100, 100, []));
        await recognizing;
        vm.HideCommand.Execute(null);
        Assert.Null(picker.Highlighted);

        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        Assert.Equal(target, picker.Highlighted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_reselection_restores_only_a_highlight_that_was_waiting_for_recognition(bool alreadyRecognized)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var picker = new DeferredPicker();
        var (vm, _, _, _) = CreateVm(picker);
        using var lifetime = vm;
        vm.Selection.SelectedWindow = target;
        Assert.Equal(target, picker.Highlighted);
        if (alreadyRecognized) await vm.RecognizeCommand.ExecuteAsync(null);

        var selecting = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        Assert.Null(picker.Highlighted);
        picker.Completion.SetResult(null);
        await selecting;

        Assert.Equal(alreadyRecognized ? null : target, picker.Highlighted);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("clear")]
    [InlineData("dispose")]
    public void A_pending_highlight_preserves_its_binding_while_unavailable_and_is_cleared_at_the_end_of_its_lifetime(string action)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var picker = new FakeWindowPicker(target);
        var tracker = new FakeTargetWindowTracker();
        var (vm, _, _, _) = CreateVm(picker, tracker);
        using var lifetime = vm;
        vm.Selection.SelectedWindow = target;
        Assert.Equal(target, picker.Highlighted);

        tracker.Publish(tracker.Current! with { IsMinimized = true });
        Assert.Equal(target, picker.Highlighted);
        tracker.Publish(tracker.Current! with { IsMinimized = false });
        Assert.Equal(target, picker.Highlighted);

        if (action == "close") tracker.Publish(tracker.Current! with { IsValid = false });
        if (action == "clear") vm.Selection.SelectedWindow = null;
        if (action == "dispose") vm.Dispose();
        Assert.Null(picker.Highlighted);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("switch")]
    [InlineData("clear")]
    [InlineData("dispose")]
    public async Task Ending_a_session_stops_capture_and_rejects_late_manual_results(string action)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var recognizer = new DeferredRecognizer();
        var capture = new RecordingCapture();
        var controller = new RecordingAutoController();
        var selector = new FakeRegionSelector();
        var (vm, overlay, _, logger) = CreateVm(new FakeWindowPicker(target), recognizer: recognizer, capture: capture, controller: controller, regionSelector: selector);
        vm.Selection.SelectedWindow = target;
        vm.AutoRecognitionEnabled = true;
        var stoppedBefore = capture.StopCount;
        var pending = vm.RecognizeCommand.ExecuteAsync(null);
        if (action == "hide") vm.HideCommand.Execute(null);
        if (action == "switch") vm.Selection.SelectedWindow = new WindowTarget((nint)3, "Other", target.Bounds);
        if (action == "clear") vm.Selection.SelectedWindow = null;
        if (action == "dispose") { vm.Dispose(); vm.Dispose(); }
        var status = vm.Status;
        recognizer.Completion.SetResult(new WordRecognitionResult(100, 100, []));
        await pending;
        Assert.True(recognizer.CancellationToken.IsCancellationRequested);
        Assert.Equal(stoppedBefore + 1, capture.StopCount);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(0, overlay.ShowCount);
        Assert.Equal(status, vm.Status);
        Assert.Equal(0, logger.ErrorCount);
        Assert.Equal(0, selector.FlashCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.1,0.6,0.8,0.3")]
    public async Task Empty_manual_recognition_flashes_the_saved_region_and_can_be_repeated(string? savedRegion)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 200, 100));
        var recognizer = new DeferredRecognizer();
        recognizer.Completion.SetResult(new WordRecognitionResult(200, 100, []));
        var selector = new FakeRegionSelector();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), recognizer: recognizer,
            regionSelector: selector, settings: new FakeSettings(savedRegion));
        vm.Selection.SelectedWindow = target;

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.Status_NoWords + ":0", vm.Status);
        Assert.Equal(1, selector.FlashCount);
        Assert.Equal(target, selector.LastFlashTarget);
        Assert.Equal(savedRegion, selector.LastFlashRegion?.Serialize());

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal(2, selector.FlashCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_empty_results_do_not_flash_while_manual_empty_results_still_do(bool manualStartsEmpty)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 200, 100));
        var empty = new WordRecognitionResult(200, 100, []);
        var text = new WordRecognitionResult(200, 100,
            [new OcrLine([new OcrWord("日", new ScreenRect(10, 5, 10, 10))])]);
        var recognizer = new SequenceRecognizer(manualStartsEmpty ? empty : text, empty, empty, text, empty, empty, empty);
        var selector = new FakeRegionSelector();
        var controller = new RecordingAutoController();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), recognizer: recognizer,
            controller: controller, regionSelector: selector);
        vm.Selection.SelectedWindow = target;
        vm.AutoRecognitionEnabled = true;

        await vm.RecognizeCommand.ExecuteAsync(null);
        var expectedWarnings = manualStartsEmpty ? 1 : 0;
        Assert.Equal(expectedWarnings, selector.FlashCount);
        await controller.ChangeAsync();
        Assert.Equal(expectedWarnings, selector.FlashCount);
        await controller.ChangeAsync();
        Assert.Equal(expectedWarnings, selector.FlashCount);
        var stoppedBefore = selector.StopFlashingCount;
        await controller.ChangeAsync();
        Assert.Equal(stoppedBefore + 1, selector.StopFlashingCount);
        await controller.ChangeAsync();
        Assert.Equal(expectedWarnings, selector.FlashCount);
        await controller.ChangeAsync();
        Assert.Equal(expectedWarnings, selector.FlashCount);

        await vm.RecognizeCommand.ExecuteAsync(null);
        Assert.Equal(expectedWarnings + 1, selector.FlashCount);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("switch")]
    [InlineData("clear")]
    [InlineData("dispose")]
    public async Task Ending_a_session_stops_empty_result_feedback(string action)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 200, 100));
        var recognizer = new DeferredRecognizer();
        recognizer.Completion.SetResult(new WordRecognitionResult(200, 100, []));
        var selector = new FakeRegionSelector();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), recognizer: recognizer, regionSelector: selector);
        vm.Selection.SelectedWindow = target;
        await vm.RecognizeCommand.ExecuteAsync(null);
        var stoppedBefore = selector.StopFlashingCount;

        if (action == "hide") vm.HideCommand.Execute(null);
        if (action == "switch") vm.Selection.SelectedWindow = new WindowTarget((nint)3, "Other", target.Bounds);
        if (action == "clear") vm.Selection.SelectedWindow = null;
        if (action == "dispose") vm.Dispose();

        Assert.Equal(stoppedBefore + 1, selector.StopFlashingCount);
    }

    [Fact]
    public async Task Manual_capture_failure_stops_capture_and_reports_its_stable_error_code()
    {
        var recognizer = new DeferredRecognizer();
        var capture = new RecordingCapture();
        var (vm, overlay, _, logger) = CreateVm(new FakeWindowPicker(), recognizer: recognizer, capture: capture);
        vm.Selection.SelectedWindow = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var stoppedBefore = capture.StopCount;
        var pending = vm.RecognizeCommand.ExecuteAsync(null);
        recognizer.Completion.SetException(new WindowsPlatformException(ErrorCodes.WindowCaptureTimedOut, "Native capture timed out."));
        await pending;
        Assert.Equal(stoppedBefore + 1, capture.StopCount);
        Assert.Equal(ErrorCodes.WindowCaptureTimedOut, vm.Status);
        Assert.Equal(0, overlay.ShowCount);
        Assert.Equal(1, logger.ErrorCount);
    }

    [Fact]
    public void A_new_view_model_has_no_implicit_selection()
    {
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100))));
        Assert.Null(vm.Selection.SelectedWindow);
    }

    [Fact]
    public async Task Recognize_without_selection_warns_user()
    {
        var (vm, overlay, _, _) = CreateVm(new FakeWindowPicker());

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.Status_SelectTargetFirst, vm.Status);
        Assert.Equal(0, overlay.ShowCount);
    }

    [Fact]
    public async Task Recognize_reports_count_and_shows_overlay()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var selector = new FakeRegionSelector();
        var (vm, overlay, _, _) = CreateVm(new FakeWindowPicker(target), regionSelector: selector);
        vm.Selection.SelectedWindow = target;

        await vm.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.Status_WordsRecognized}:1", vm.Status);
        Assert.Equal(2, overlay.ShowCount);
        Assert.NotNull(overlay.LastSession);
        Assert.Equal(0, selector.FlashCount);
    }

    [Fact]
    public void Hide_clears_overlay_and_updates_status()
    {
        var (vm, overlay, _, _) = CreateVm(new FakeWindowPicker());

        vm.HideCommand.Execute(null);

        Assert.Equal(ResourceKeys.Status_Hidden, vm.Status);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void Culture_changed_re_derives_status_from_current_state()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var (vm, _, localizer, _) = CreateVm(new FakeWindowPicker(target));
        vm.Selection.SelectedWindow = target;
        Assert.Equal($"{ResourceKeys.Status_Selected}:Other", vm.Status);

        // Simulate the rendered result changing after a culture switch; the ViewModel should recompute Status accordingly.
        localizer.Suffix = "!";
        localizer.RaiseCultureChanged();

        Assert.Equal($"{ResourceKeys.Status_Selected}:Other!", vm.Status);
    }

    [Fact]
    public void Selecting_window_attaches_tracker_and_clearing_selection_detaches_it()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var tracker = new FakeTargetWindowTracker();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(), tracker);

        vm.Selection.SelectedWindow = target;
        vm.Selection.SelectedWindow = null;

        Assert.Equal(1, tracker.AttachCount);
        Assert.Equal(1, tracker.DetachCount);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Set_recognition_region_shows_selector_for_selected_window()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var tracker = new FakeTargetWindowTracker();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), tracker);
        vm.Selection.SelectedWindow = target;
        FakeRegionSelector.ShowCount = 0;

        vm.SetRecognitionRegionCommand.Execute(null);

        Assert.Equal(1, FakeRegionSelector.ShowCount);
        Assert.Equal("Other", FakeRegionSelector.LastTarget?.Title);
        Assert.Equal(1, tracker.RefreshCount);
    }

    [Fact]
    public void Set_recognition_region_without_window_shows_hint()
    {
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker());

        vm.SetRecognitionRegionCommand.Execute(null);

        Assert.Equal(ResourceKeys.Status_SelectTargetFirst, vm.Status);
    }

    [Fact]
    public void Set_recognition_region_refresh_failure_logs_and_reports_target_error()
    {
        var target = new WindowTarget((nint)2, "Other", new ScreenRect(0, 0, 200, 100));
        var tracker = new FakeTargetWindowTracker { ThrowOnRefresh = true };
        var (vm, _, _, logger) = CreateVm(new FakeWindowPicker(target), tracker);
        vm.Selection.SelectedWindow = target;
        FakeRegionSelector.ShowCount = 0;

        vm.SetRecognitionRegionCommand.Execute(null);

        Assert.Equal(1, logger.ErrorCount);
        Assert.Equal(ErrorCodes.TargetWindowUnavailable, vm.Status);
        Assert.Equal(0, FakeRegionSelector.ShowCount);
    }

    [Fact]
    public async Task Picker_failure_logs_once_and_preserves_the_selection()
    {
        var (vm, _, _, logger) = CreateVm(new ThrowingPicker());
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        vm.Selection.SelectedWindow = target;

        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);

        Assert.Equal(target, vm.Selection.SelectedWindow);
        Assert.False(vm.Selection.IsSelectingWindow);
        Assert.Equal(1, logger.ErrorCount);
        Assert.Equal(ErrorCodes.WindowSelectionFailed, vm.Status);
    }

    [Fact]
    public async Task Cancelling_a_picker_keeps_the_active_session_and_thumbnail()
    {
        var picker = new DeferredPicker();
        var controller = new RecordingAutoController();
        var (vm, _, _, _) = CreateVm(picker, controller: controller);
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        vm.Selection.SelectedWindow = target;
        vm.AutoRecognitionEnabled = true;
        await vm.RecognizeCommand.ExecuteAsync(null);

        var picking = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        Assert.True(vm.Selection.IsPickingWindow);
        Assert.False(vm.RecognizeCommand.CanExecute(null));
        Assert.False(vm.SetRecognitionRegionCommand.CanExecute(null));
        picker.Completion.SetResult(null);
        await picking;

        Assert.Equal(target, vm.Selection.SelectedWindow);
        Assert.True(controller.IsRunning);
        Assert.False(vm.Selection.IsSelectingWindow);
    }

    [Fact]
    public async Task Selection_captures_the_full_client_once_without_recognition_and_stops_preview_capture()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(-200, 40, 100, 100));
        var capture = new PreviewCapture();
        var controller = new RecordingAutoController();
        var (vm, overlay, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture, controller: controller);
        var previews = new List<WindowPreviewEventArgs>();
        vm.Selection.PreviewReady += (_, preview) => previews.Add(preview);
        vm.AutoRecognitionEnabled = true;

        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);

        var preview = Assert.Single(previews);
        Assert.Equal(target, vm.Selection.SelectedWindow);
        Assert.Equal(target.Bounds, preview.Bounds);
        Assert.Equal(vm.Selection.SelectionVersion, preview.Version);
        Assert.True(preview.Animate);
        Assert.True(vm.Selection.HasPreview);
        Assert.Null(capture.Region);
        Assert.Equal(1, capture.Calls);
        Assert.True(capture.StopCount >= 1);
        Assert.Equal(0, overlay.ShowCount);
        Assert.Equal(0, controller.StartCount);
    }

    [Fact]
    public async Task Reselecting_the_same_window_ends_recognition_and_replaces_the_preview()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture();
        var controller = new RecordingAutoController();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture, controller: controller);
        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        var version = vm.Selection.SelectionVersion;
        vm.AutoRecognitionEnabled = true;
        await vm.RecognizeCommand.ExecuteAsync(null);
        Assert.True(controller.IsRunning);

        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);

        Assert.False(controller.IsRunning);
        Assert.True(vm.Selection.SelectionVersion > version);
        Assert.Equal(2, capture.Calls);
        Assert.True(vm.Selection.HasPreview);
    }

    [Fact]
    public async Task Failed_preview_keeps_the_new_target_and_retry_never_starts_recognition_or_flight()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture { Failure = new InvalidOperationException("Capture failed.") };
        var (vm, overlay, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture);
        var previews = new List<WindowPreviewEventArgs>();
        vm.Selection.PreviewReady += (_, preview) => previews.Add(preview);
        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);

        Assert.Equal(target, vm.Selection.SelectedWindow);
        Assert.True(vm.Selection.HasPreviewError);
        Assert.False(vm.Selection.HasPreview);
        Assert.True(vm.Selection.RetryPreviewCommand.CanExecute(null));
        capture.Failure = null;
        await vm.Selection.RetryPreviewCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(previews).Animate);
        Assert.True(vm.Selection.HasPreview);
        Assert.False(vm.Selection.HasPreviewError);
        Assert.Equal(0, overlay.ShowCount);
    }

    [Fact]
    public async Task Disposing_during_capture_rejects_a_late_frame()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture);
        var published = 0;
        vm.Selection.PreviewReady += (_, _) => published++;
        var selecting = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        Assert.Equal(1, capture.Calls);
        vm.Dispose();
        capture.Pending.SetResult(new CapturedFrame(100, 100, new byte[40000]));
        await selecting;

        Assert.Equal(0, published);
        Assert.False(vm.Selection.HasPreview);
        Assert.False(vm.Selection.IsSelectingWindow);
    }

    [Fact]
    public async Task Minimizing_and_restoring_a_target_does_not_refresh_its_static_preview()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var tracker = new FakeTargetWindowTracker();
        var capture = new PreviewCapture();
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), tracker, capture: capture);
        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        tracker.Publish(tracker.Current! with { IsMinimized = true });
        Assert.True(vm.Selection.HasPreview);
        Assert.Equal(ResourceKeys.WindowPicker_Unavailable, vm.Selection.TargetStatus);
        tracker.Publish(tracker.Current! with { IsMinimized = false });
        Assert.Equal(ResourceKeys.WindowPicker_Selected, vm.Selection.TargetStatus);
        Assert.Equal(1, capture.Calls);
    }

    [Fact]
    public async Task Closing_the_original_target_does_not_cancel_picking_another_window()
    {
        var original = new WindowTarget((nint)2, "Original", new ScreenRect(0, 0, 100, 100));
        var next = new WindowTarget((nint)3, "Next", original.Bounds);
        var picker = new DeferredPicker();
        var tracker = new FakeTargetWindowTracker();
        var (vm, _, _, _) = CreateVm(picker, tracker, capture: new PreviewCapture());
        vm.Selection.SelectedWindow = original;
        var selecting = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        tracker.Publish(tracker.Current! with { IsValid = false });
        Assert.Null(vm.Selection.SelectedWindow);
        Assert.True(vm.Selection.IsPickingWindow);
        picker.Completion.SetResult(next);
        await selecting;
        Assert.Equal(next, vm.Selection.SelectedWindow);
        Assert.True(vm.Selection.HasPreview);
    }

    [Fact]
    public async Task Closing_the_committed_target_rejects_a_late_preview()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var tracker = new FakeTargetWindowTracker();
        var capture = new PreviewCapture { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), tracker, capture: capture);
        var published = 0;
        vm.Selection.PreviewReady += (_, _) => published++;
        var selecting = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        tracker.Publish(tracker.Current! with { IsValid = false });
        capture.Pending.SetResult(new CapturedFrame(100, 100, new byte[40000]));
        await selecting;
        Assert.Null(vm.Selection.SelectedWindow);
        Assert.False(vm.Selection.HasPreview);
        Assert.Equal(0, published);
        Assert.Equal(ResourceKeys.WindowPicker_Closed, vm.Selection.TargetStatus);
    }

    [Fact]
    public async Task Preview_retry_cannot_stop_an_active_recognition_session()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture { Failure = new InvalidOperationException("Capture failed.") };
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture);
        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        await vm.RecognizeCommand.ExecuteAsync(null);
        var stopped = capture.StopCount;
        Assert.False(vm.Selection.RetryPreviewCommand.CanExecute(null));
        await vm.Selection.RetryPreviewCommand.ExecuteAsync(null);
        Assert.Equal(stopped, capture.StopCount);
        vm.HideCommand.Execute(null);
        Assert.True(vm.Selection.RetryPreviewCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(ErrorCodes.WindowCaptureTimedOut)]
    [InlineData(ErrorCodes.WindowCaptureNotSupported)]
    [InlineData(ErrorCodes.WindowCaptureFailed)]
    public async Task Preview_failures_keep_their_stable_localized_code_and_stop_capture(string code)
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture { Failure = new WindowsPlatformException(code, "Native capture failed.") };
        var (vm, _, _, _) = CreateVm(new FakeWindowPicker(target), capture: capture);
        await vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        Assert.Equal(target, vm.Selection.SelectedWindow);
        Assert.True(vm.Selection.HasPreviewError);
        Assert.Equal(code, vm.Selection.PreviewError);
        Assert.True(capture.StopCount >= 1);
    }

    [Fact]
    public async Task Cancelling_preview_preserves_the_committed_target_and_allows_retry()
    {
        var target = new WindowTarget((nint)2, "Target", new ScreenRect(0, 0, 100, 100));
        var capture = new PreviewCapture { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (vm, _, _, logger) = CreateVm(new FakeWindowPicker(target), capture: capture);
        var selecting = vm.Selection.SelectWindowCommand.ExecuteAsync(null);
        vm.Selection.CancelWindowSelection();
        capture.Pending.SetResult(new CapturedFrame(100, 100, new byte[40000]));
        await selecting;
        Assert.Equal(target, vm.Selection.SelectedWindow);
        Assert.False(vm.Selection.HasPreview);
        Assert.True(vm.Selection.RetryPreviewCommand.CanExecute(null));
        Assert.Equal(0, logger.ErrorCount);
    }

    private static (RecognitionPageViewModel ViewModel, FakeOverlay Overlay, FakeStringLocalizer Localizer, FakeLogger Logger) CreateVm(
        IWindowPicker picker,
        ITargetWindowTracker? tracker = null,
        IWindowWordRecognizer? recognizer = null,
        IWindowFrameCapture? capture = null,
        IAutoRecognitionController? controller = null,
        IRegionSelector? regionSelector = null,
        ISettingsService? settings = null,
        KotobaSenpai.App.Support.SupportService? support = null)
    {
        var overlay = new FakeOverlay();
        var workflow = new WordOverlayApplicationService(
            recognizer ?? new FakeRecognizer(),
            new WordGroupingService(new StubTokenizer(), new SentenceSegmenter(), new StubSpanResolver()),
            overlay, KotobaSenpai.TestSupport.PhraseAnalysisStub.Create());
        var localizer = new FakeStringLocalizer();
        var resolver = new UserMessageResolver(localizer);
        var logger = new FakeLogger();
        return (new RecognitionPageViewModel(new TargetSelectionViewModel(picker, localizer, resolver, logger, frame => frame, tracker, capture), workflow, regionSelector ?? new FakeRegionSelector(), settings ?? new FakeSettings(), localizer, resolver, logger, tracker, controller, capture, support), overlay, localizer, logger);
    }

    private sealed class FakeWindowPicker : IWindowPicker
    {
        private readonly IReadOnlyList<WindowTarget> _windows;
        public FakeWindowPicker(params WindowTarget[] windows) => _windows = windows;
        public WindowTarget? Highlighted { get; private set; }
        public void Highlight(WindowTarget? target) => Highlighted = target;
        public Task<WindowTarget?> PickAsync(CancellationToken cancellationToken = default) => Task.FromResult(_windows.FirstOrDefault());
    }

    private sealed class ThrowingPicker : IWindowPicker
    {
        public void Highlight(WindowTarget? target) { }
        public Task<WindowTarget?> PickAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Picker failed.");
    }

    private sealed class DeferredPicker : IWindowPicker
    {
        public WindowTarget? Highlighted { get; private set; }
        public void Highlight(WindowTarget? target) => Highlighted = target;
        public TaskCompletionSource<WindowTarget?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WindowTarget?> PickAsync(CancellationToken cancellationToken = default) => Completion.Task;
    }

    private sealed class PreviewCapture : IWindowFrameCapture
    {
        public int Calls { get; private set; }
        public int StopCount { get; private set; }
        public ScreenRect? Region { get; private set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource<CapturedFrame>? Pending { get; set; }
        public void Stop() => StopCount++;
        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
        {
            Calls++;
            Region = region;
            if (Failure is not null) throw Failure;
            return Pending?.Task ?? Task.FromResult(new CapturedFrame(100, 100, new byte[40000]));
        }
    }

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null) =>
            Task.FromResult(new WordRecognitionResult(100, 50,
                [new OcrLine([new OcrWord("日", new ScreenRect(10, 5, 10, 10))])]));
    }

    private sealed class DeferredRecognizer : IWindowWordRecognizer
    {
        public TaskCompletionSource<WordRecognitionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CancellationToken { get; private set; }
        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
        {
            CancellationToken = cancellationToken;
            return Completion.Task;
        }
    }

    private sealed class RecordingCapture : IWindowFrameCapture
    {
        public int StopCount { get; private set; }
        public void Stop() => StopCount++;
        public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
            => throw new NotSupportedException();
    }

    private sealed class RecordingAutoController : IAutoRecognitionController
    {
        private Func<CancellationToken, Task>? _onChange;
        public int StartCount { get; private set; }
        public bool IsRunning { get; private set; }
        public void Start(WindowTarget target, Func<CancellationToken, Task>? onChange = null) { StartCount++; IsRunning = true; _onChange = onChange; }
        public Task ChangeAsync() => _onChange!(CancellationToken.None);
        public void Stop() => IsRunning = false;
        public void Dispose() => Stop();
    }

    private sealed class FakeRegionSelector : IRegionSelector
    {
        public static WindowTarget? LastTarget;
        public static int ShowCount;
        public int FlashCount { get; private set; }
        public int StopFlashingCount { get; private set; }
        public WindowTarget? LastFlashTarget { get; private set; }
        public RecognitionRegion? LastFlashRegion { get; private set; }
        public void Show(WindowTarget target, RecognitionRegion? initial = null) { LastTarget = target; ShowCount++; }
        public void Flash(WindowTarget target, RecognitionRegion? region = null)
        {
            FlashCount++;
            LastFlashTarget = target;
            LastFlashRegion = region;
        }
        public void StopFlashing() => StopFlashingCount++;
    }

    private sealed class FakeSettings(string? region = null) : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => key == RecognitionRegion.SettingsKey ? region : null;
        public override void SetValue(string key, string? value) { }
    }

    private sealed class SequenceRecognizer(params WordRecognitionResult[] results) : IWindowWordRecognizer
    {
        private readonly Queue<WordRecognitionResult> _results = new(results);
        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target,
            CancellationToken cancellationToken = default, ScreenRect? region = null)
            => Task.FromResult(_results.Dequeue());
    }

    private sealed class StubTokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text)
            => [new Token("日", "日", "日", "日", "日", "日", new PartsOfSpeech("", "", "", ""), "", "", "", 0)];
    }

    private sealed class StubSpanResolver : ITokenSpanResolver
    {
        public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens) => [];
    }

    private sealed class FakeOverlay : IOverlayRenderer
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public WordOverlaySession? LastSession { get; private set; }

        public void Show(WordOverlaySession session, Func<bool>? canRender = null)
        {
            ShowCount++;
            LastSession = session;
        }

        public void Hide() => HideCount++;
    }

    private sealed class FakeTargetWindowTracker : ITargetWindowTracker
    {
        public event EventHandler<TargetWindowSnapshot>? Changed;

        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }
        public int RefreshCount { get; private set; }
        public bool ThrowOnRefresh { get; set; }
        public TargetWindowSnapshot? Current { get; private set; }

        public TargetWindowSnapshot Attach(WindowTarget target)
        {
            AttachCount++;
            Current = new TargetWindowSnapshot(target.Handle, target.Title, target.Bounds, 1, true, false, true);
            Changed?.Invoke(this, Current);
            return Current;
        }

        public TargetWindowSnapshot Refresh()
        {
            RefreshCount++;
            if (ThrowOnRefresh)
                throw new InvalidOperationException("refresh failed");
            return Current ?? throw new InvalidOperationException("No target attached.");
        }

        public void Publish(TargetWindowSnapshot snapshot)
        {
            Current = snapshot;
            Changed?.Invoke(this, snapshot);
        }

        public void Detach()
        {
            DetachCount++;
            Current = null;
        }

        public void Dispose() { }
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
