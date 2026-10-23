using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class RegionSelectorWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Empty_feedback_draws_a_passive_glowing_rim_with_a_transparent_center(bool savedRegion) => RunSta(() =>
    {
        var (window, target) = CreateTarget();
        var settings = new RecordingSettings();
        var selector = new RegionSelectorWindow(settings);
        try
        {
            var region = savedRegion ? new RecognitionRegion(0.1, 0.6, 0.8, 0.3) : null;
            var activeBefore = Native.GetActiveWindow();

            selector.Flash(target, region);

            Assert.True(selector.IsVisible);
            var canvas = Assert.IsType<Canvas>(selector.Content);
            var outline = Assert.Single(canvas.Children.OfType<Grid>());
            selector.UpdateLayout();
            Assert.False(canvas.IsHitTestVisible);
            Assert.False(selector.ShowActivated);
            Assert.False(selector.ShowInTaskbar);
            Assert.False(selector.Topmost);
            Assert.Equal(activeBefore, Native.GetActiveWindow());
            var handle = new WindowInteropHelper(selector).Handle;
            Assert.Equal(0x20, Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x20);
            Assert.Contains(handle, OverlayWindowRegistry.Snapshot());
            var scale = PresentationSource.FromVisual(selector)!.CompositionTarget!.TransformToDevice.M11;
            var pixels = (region ?? RecognitionRegion.Full).ToPixelRect(target.Bounds.Width, target.Bounds.Height);
            Assert.Equal(pixels.X / scale, Canvas.GetLeft(outline), precision: 3);
            Assert.Equal(pixels.Y / scale, Canvas.GetTop(outline), precision: 3);
            Assert.Equal(pixels.Width / scale, outline.Width, precision: 3);
            Assert.Equal(pixels.Height / scale, outline.Height, precision: 3);
            var image = new RenderTargetBitmap((int)Math.Ceiling(selector.Width), (int)Math.Ceiling(selector.Height),
                96, 96, PixelFormats.Pbgra32);
            image.Render(outline);
            var centerX = (int)(Canvas.GetLeft(outline) + outline.Width / 2);
            var top = (int)Canvas.GetTop(outline);
            var center = PixelAt(image, centerX, (int)(top + outline.Height / 2));
            var rim = PixelAt(image, centerX, top + 4);
            var glow = PixelAt(image, centerX, top + 10);
            Assert.Equal(0, center.Alpha);
            Assert.True(rim.Alpha > glow.Alpha && glow.Alpha > 0,
                $"The warning rim must fade into a soft halo (rim alpha: {rim.Alpha}, halo alpha: {glow.Alpha}).");
            Assert.True(glow.Red > glow.Green && glow.Red > glow.Blue, "The warning halo must remain red.");
            Assert.Equal(0, settings.WriteCount);
        }
        finally
        {
            selector.Close();
            window.Close();
        }
    });

    [Fact]
    public Task Feedback_flashes_exactly_five_times_then_hides_and_unregisters_on_close() => RunSta(() =>
    {
        var (window, target) = CreateTarget();
        var selector = new RegionSelectorWindow(new RecordingSettings());
        var handle = nint.Zero;
        try
        {
            selector.Flash(target);
            Assert.True(selector.IsVisible);
            handle = new WindowInteropHelper(selector).Handle;
            var canvas = Assert.IsType<Canvas>(selector.Content);
            var outline = Assert.Single(canvas.Children.OfType<Grid>());
            var message = Assert.Single(canvas.Children.OfType<Border>());
            var messageBlinked = false;
            var offPhases = 0;
            var pulses = outline.IsVisible ? 1 : 0;
            outline.IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue)
                    pulses++;
                else if (canvas.Children.Contains(outline))
                {
                    offPhases++;
                    messageBlinked |= !message.IsVisible || message.Opacity != 1;
                }
            };
            var frame = new DispatcherFrame();
            selector.IsVisibleChanged += (_, _) => { if (!selector.IsVisible) frame.Continue = false; };
            var timedOut = false;
            var timeout = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(10),
            };
            timeout.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
            timeout.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }

            Assert.False(timedOut, "Region feedback must finish without user interaction.");
            Assert.Equal(5, pulses);
            Assert.Equal(5, offPhases);
            Assert.False(messageBlinked, "The error message must remain visible while only the rim blinks.");
            Assert.False(selector.IsVisible);
            Assert.Empty(canvas.Children.Cast<UIElement>());
        }
        finally
        {
            selector.Close();
            window.Close();
        }
        Assert.DoesNotContain(handle, OverlayWindowRegistry.Snapshot());
    });

    [Fact]
    public Task Opening_the_editor_cancels_feedback_and_restores_mouse_input() => RunSta(() =>
    {
        var (window, target) = CreateTarget();
        var selector = new RegionSelectorWindow(new RecordingSettings());
        try
        {
            selector.Flash(target);
            Assert.True(selector.IsVisible);
            selector.StopFlashing();
            Assert.False(selector.IsVisible);

            selector.Flash(target);
            selector.Show(target);
            selector.StopFlashing();
            selector.Flash(target);

            Assert.True(selector.IsVisible);
            var canvas = Assert.IsType<Canvas>(selector.Content);
            Assert.True(canvas.IsHitTestVisible);
            Assert.Single(canvas.Children.OfType<Button>());
            var handle = new WindowInteropHelper(selector).Handle;
            Assert.Equal(0, Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x20);
        }
        finally
        {
            selector.Close();
            window.Close();
        }
    });

    [Theory]
    [InlineData("switch")]
    [InlineData("minimize")]
    [InlineData("occlude")]
    [InlineData("close")]
    public Task Losing_the_target_stops_feedback_without_showing_it_again(string change) => RunSta(() =>
    {
        var (window, target) = CreateTarget();
        var tracker = new FakeTracker();
        var initial = tracker.Attach(target);
        var selector = new RegionSelectorWindow(new RecordingSettings(), tracker: tracker);
        try
        {
            selector.Flash(target, new RecognitionRegion(0.1, 0.6, 0.8, 0.3));
            Assert.True(selector.IsVisible);
            var moved = initial with
            {
                Bounds = new ScreenRect(initial.Bounds.X + 40, initial.Bounds.Y, initial.Bounds.Width, initial.Bounds.Height),
            };
            tracker.Publish(moved);
            var handle = new WindowInteropHelper(selector).Handle;
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            Assert.Equal(moved.Bounds.X, bounds.Left);
            tracker.Publish(change switch
            {
                "switch" => moved with { Handle = (nint)999 },
                "minimize" => moved with { IsMinimized = true },
                "occlude" => moved with { IsOccluded = true },
                _ => moved with { IsValid = false },
            });

            Assert.False(selector.IsVisible);
            tracker.Publish(initial);
            Assert.False(selector.IsVisible);
        }
        finally
        {
            selector.Close();
            window.Close();
        }
    });

    [Fact]
    public Task Feedback_message_is_localized_includes_an_error_icon_and_refreshes_while_visible() => RunSta(() =>
    {
        var (window, target) = CreateTarget();
        var localizer = new FeedbackLocalizer();
        var selector = new RegionSelectorWindow(new RecordingSettings(), localizer);
        try
        {
            selector.Flash(target);
            selector.UpdateLayout();
            var canvas = Assert.IsType<Canvas>(selector.Content);
            var message = Assert.Single(canvas.Children.OfType<Border>());
            var content = Assert.IsType<Grid>(message.Child);
            var text = Assert.Single(content.Children.OfType<TextBlock>());
            var icon = Assert.Single(content.Children.OfType<Image>());

            Assert.Equal(localizer.Translation, text.Text);
            Assert.Equal("Region_NoJapanese", localizer.LastKey);
            Assert.NotNull(icon.Source);
            Assert.Equal(0, Grid.GetColumn(icon));
            Assert.Equal(1, Grid.GetColumn(text));
            Assert.True(message.IsVisible);

            localizer.ChangeTranslation("Updated language: choose a different area.");

            Assert.Equal(localizer.Translation, text.Text);
            selector.Close();
            Assert.Equal(0, localizer.SubscriberCount);
        }
        finally
        {
            if (selector.IsVisible)
                selector.Close();
            window.Close();
        }
    });

    private static (byte Blue, byte Green, byte Red, byte Alpha) PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static (Window Window, WindowTarget Target) CreateTarget()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
            Left = 100, Top = 100, Width = 800, Height = 450, Background = Brushes.White,
        };
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        Assert.True(Native.GetWindowRect(handle, out var bounds));
        return (window, new WindowTarget(handle, "Target",
            new ScreenRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)));
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class RecordingSettings : ISettingsService
    {
        public int WriteCount { get; private set; }
        public string? GetValue(string key) => null;
        public void SetValue(string key, string? value) => WriteCount++;
    }

    private sealed class FeedbackLocalizer : IStringLocalizer
    {
        private EventHandler? _cultureChanged;
        public string Translation { get; private set; } = "No recognizable Japanese was found in this area. Please select a different area.";
        public string? LastKey { get; private set; }
        public int SubscriberCount => _cultureChanged?.GetInvocationList().Length ?? 0;
        public string Get(string key, params object[] args) { LastKey = key; return Translation; }
        public event EventHandler? CultureChanged
        {
            add => _cultureChanged += value;
            remove => _cultureChanged -= value;
        }
        public void ChangeTranslation(string text) { Translation = text; _cultureChanged?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeTracker : ITargetWindowTracker
    {
        public event EventHandler<TargetWindowSnapshot>? Changed;
        public TargetWindowSnapshot? Current { get; private set; }
        public TargetWindowSnapshot Attach(WindowTarget target)
        {
            var snapshot = new TargetWindowSnapshot(target.Handle, target.Title, target.Bounds, 1, true, false, true);
            Publish(snapshot);
            return snapshot;
        }
        public void Publish(TargetWindowSnapshot snapshot) { Current = snapshot; Changed?.Invoke(this, snapshot); }
        public TargetWindowSnapshot Refresh() => Current!;
        public void Detach() => Current = null;
        public void Dispose() { }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern nint GetActiveWindow();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint handle, int index);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint handle, out Rect rect);
    }
}
