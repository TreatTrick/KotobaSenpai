using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Platform.Windows.Overlay;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class WindowPickerTests
{
    [Fact]
    public Task A_closed_highlight_is_not_recreated_until_the_selection_binding_is_explicitly_reset() => RunSta(() =>
    {
        var target = new Window { Title = "Target", Width = 240, Height = 180, ShowActivated = false, ShowInTaskbar = false };
        var logger = new RecordingLogger();
        using var picker = new Win32WindowPicker(new FakeLocalizer(), logger);
        try
        {
            target.Show();
            var candidate = Win32WindowPicker.ReadCandidate(new WindowInteropHelper(target).Handle, 0)!;
            nint[] Helpers() => OverlayWindowRegistry.Snapshot()
                .Where(handle => Native.GetWindowThreadProcessId(handle, out _) == candidate.ThreadId).ToArray();
            picker.Highlight(candidate.Target, 0);
            var handle = Assert.Single(Helpers());
            var outline = Assert.IsType<Win32WindowPicker.RainbowWindow>(Window.GetWindow(HwndSource.FromHwnd(handle)!.RootVisual));
            outline.Close();

            picker.Highlight(candidate.Target, 0);
            Assert.Empty(Helpers());
            picker.Highlight(null);
            picker.Highlight(candidate.Target, 0);
            Assert.Single(Helpers());
            picker.Dispose();
            Assert.Empty(Helpers());
            Assert.Empty(logger.Errors);
        }
        finally { target.Close(); }
    });

    [Fact]
    public Task A_selected_outline_follows_geometry_and_visibility_and_releases_itself_when_the_target_closes() => RunSta(() =>
    {
        var target = new Window { Title = "Target", Width = 240, Height = 180, Left = 30, Top = 40, ShowActivated = false, ShowInTaskbar = false };
        var outline = new Win32WindowPicker.RainbowWindow();
        var logger = new RecordingLogger();
        var closed = false;
        outline.Closed += (_, _) => closed = true;
        nint handle = 0;
        try
        {
            target.Show();
            var candidate = Win32WindowPicker.ReadCandidate(new WindowInteropHelper(target).Handle, 0)!;
            outline.Follow(candidate, logger);
            handle = new WindowInteropHelper(outline).Handle;
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(outline.IsVisible);
            var canvas = Assert.IsType<Canvas>(outline.Content);
            Assert.Equal(Visibility.Collapsed, canvas.Children[1].Visibility);
            var before = VisualTreeHelper.GetDrawing(canvas.Children[0])!.Bounds;
            Assert.False(before.IsEmpty);

            target.Left += 80;
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(VisualTreeHelper.GetDrawing(canvas.Children[0])!.Bounds.Left > before.Left);
            target.Hide();
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.False(outline.IsVisible);
            target.Show();
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(outline.IsVisible);

            target.Close();
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(closed);
            Assert.DoesNotContain(handle, OverlayWindowRegistry.Snapshot());
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.False(outline.IsVisible);
            Assert.Empty(logger.Errors);
        }
        finally { if (!closed) outline.Close(); target.Close(); }
    });

    [Fact]
    public Task Native_hit_testing_respects_the_front_window_and_owned_dialog_root() => RunSta(() =>
    {
        var target = new Window { Title = "Target", Width = 300, Height = 240, Left = 30, Top = 40, Topmost = true, ShowActivated = false, ShowInTaskbar = false };
        var dialog = new Window { Title = "Dialog", Width = 160, Height = 140, Left = 60, Top = 80, Topmost = true, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            target.Show();
            dialog.Owner = target;
            dialog.Show();
            var handle = new WindowInteropHelper(dialog).Handle;
            var candidate = Win32WindowPicker.ReadCandidate(handle, 0)!;
            var x = candidate.Target.Bounds.X + 30;
            var y = candidate.Target.Bounds.Y + 30;
            Assert.Equal(handle, Win32WindowPicker.ResolveAt(x, y, 0)?.Target.Handle);
            dialog.Hide();
            Assert.Equal(new WindowInteropHelper(target).Handle, Win32WindowPicker.ResolveAt(x, y, 0)?.Target.Handle);
        }
        finally { dialog.Close(); target.Close(); }
    });

    [Fact]
    public Task Native_candidates_keep_client_geometry_accept_untitled_windows_and_exclude_our_process() => RunSta(() =>
    {
        var window = new Window { Title = string.Empty, Width = 240, Height = 180, Left = 30, Top = 40, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            var candidate = Assert.IsType<WindowPickerCandidate>(Win32WindowPicker.ReadCandidate(handle, 0));
            Assert.Equal(handle, candidate.Target.Handle);
            Assert.Empty(candidate.Target.Title);
            Assert.True(candidate.Target.Bounds.Y > candidate.FrameBounds.Y);
            Assert.True(candidate.Target.Bounds.Height < candidate.FrameBounds.Height);
            Assert.Null(Win32WindowPicker.ReadCandidate(handle, Environment.ProcessId));
            window.Hide();
            Assert.Null(Win32WindowPicker.ReadCandidate(handle, 0));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Desktop_surfaces_are_native_click_through_registered_and_map_negative_pixels() => RunSta(() =>
    {
        var active = Native.GetActiveWindow();
        var surface = new DesktopOverlayWindow();
        nint handle = 0;
        try
        {
            surface.ShowOverDesktop();
            handle = new WindowInteropHelper(surface).Handle;
            var style = Native.GetWindowLongPtr(handle, -20).ToInt64();
            Assert.Equal(0x08000020, style & 0x08000020);
            Assert.Equal(active, Native.GetActiveWindow());
            Assert.Contains(handle, OverlayWindowRegistry.Snapshot());
            Assert.Null(Win32WindowPicker.ReadCandidate(handle, 0));
            var pixels = new Rect(-240, -100, 160, 90);
            var mapped = surface.FromScreenPixels(pixels);
            Assert.Equal(pixels.TopLeft, surface.PointToScreen(mapped.TopLeft));
            Assert.Equal(pixels.BottomRight, surface.PointToScreen(mapped.BottomRight));
        }
        finally { surface.Close(); }
        Assert.DoesNotContain(handle, OverlayWindowRegistry.Snapshot());
    });

    [Fact]
    public void Escape_waits_for_a_swallowed_mouse_press_to_be_released()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.Press(target);
        gesture.PressEscape();
        Assert.False(gesture.ReleaseEscape());
        Assert.False(gesture.IsCancellationReady);
        Assert.Null(gesture.Release(target));
        Assert.True(gesture.IsCancellationReady);
    }

    [Fact]
    public void A_click_while_escape_is_held_waits_for_escape_release_without_committing()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.PressEscape();
        gesture.Press(target);
        Assert.Null(gesture.Release(target));
        Assert.False(gesture.IsCancellationReady);
        Assert.True(gesture.ReleaseEscape());
    }

    [Fact]
    public void A_click_commits_only_after_release_over_the_same_window_identity()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.Press(target);

        Assert.Null(gesture.Release(Candidate(11, 20, 30)));
        Assert.Null(gesture.Release(target));

        gesture.Press(target);
        var moved = target with { Target = target.Target.WithBounds(new ScreenRect(-300, 40, 120, 80)) };
        Assert.Same(moved, gesture.Release(moved));
        Assert.Null(gesture.Release(moved));
    }

    [Theory]
    [InlineData(21, 30)]
    [InlineData(20, 31)]
    public void A_reused_handle_cannot_complete_the_original_click(uint processId, uint threadId)
    {
        var gesture = new WindowPickGesture();
        gesture.Press(Candidate(10, 20, 30));

        Assert.Null(gesture.Release(Candidate(10, processId, threadId)));
    }

    [Fact]
    public void An_invalid_press_or_release_does_not_select_a_window()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.Press(null);
        Assert.Null(gesture.Release(target));
        gesture.Press(target);
        Assert.Null(gesture.Release(null));
        Assert.Null(gesture.Release(target));
    }

    private static WindowPickerCandidate Candidate(int handle, uint processId, uint threadId)
    {
        var bounds = new ScreenRect(-100, 20, 120, 80);
        return new(new WindowTarget((nint)handle, "Target", bounds), bounds, processId, threadId, 96);
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static class Native
    {
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll")] internal static extern nint GetActiveWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
        public event EventHandler? CultureChanged { add { } remove { } }
        public string Get(string key, params object[] args) => key;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<Exception?> Errors { get; } = [];
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (level == LogLevel.Error) Errors.Add(exception);
        }
    }
}
