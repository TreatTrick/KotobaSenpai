using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KotobaSenpai.App.Controls;

namespace KotobaSenpai.App.Tests;

public sealed class FluentLightPresentationTests
{
    [Fact]
    public Task Pointer_light_does_not_form_uniform_concentric_color_bands() => RunSta(() =>
    {
        WithPointerWash((baseline, lit, center) =>
        {
            var circle = new[] { (130, 0), (126, 32), (120, 50), (104, 78), (78, 104), (50, 120), (32, 126), (0, 130) };
            var levels = new List<int>();
            foreach (var (x, y) in circle)
            foreach (var signX in new[] { -1, 1 })
            foreach (var signY in new[] { -1, 1 })
                levels.Add(Difference(baseline, lit, center + signX * x, center + signY * y));

            Assert.True(levels.Distinct().Count() >= 2,
                $"The wash should distribute subpixel brightness across a ring instead of forming one flat band; found {levels.Distinct().Count()} levels.");
            Assert.InRange(levels.Max() - levels.Min(), 1, 6);
        });
    });

    [Fact]
    public Task Pointer_light_has_a_faint_outer_tail_that_reaches_the_background_smoothly() => RunSta(() =>
    {
        WithPointerWash((baseline, lit, center) =>
        {
            Assert.True(Difference(baseline, lit, center, center) > 10);
            var tail = Enumerable.Range(190, 25)
                .SelectMany(radius => Enumerable.Range(-20, 41)
                    .Select(y => Difference(baseline, lit, center + radius, center + y))).Average();
            Assert.InRange(tail, 0, 1);
            Assert.Equal(0, Difference(baseline, lit, center + 225, center));
        });
    });

    [Fact]
    public Task Shared_light_reaches_neighbors_and_button_glow_preserves_input_and_layout() => RunSta(() =>
    {
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(32, 33, 39)) };
        var left = AddButton(canvas, 20, 40);
        var right = AddButton(canvas, 180, 40);
        var host = new FluentLightHost { Child = canvas };
        var window = Show(host);
        try
        {
            var baseline = Render(host);
            host.UpdatePointer(new Point(170, 58));
            host.Advance(1, animate: false);
            var lit = Render(host);
            Assert.True(Difference(baseline, lit, 159, 58) > 10);
            Assert.True(Difference(baseline, lit, 180, 58) > 10);
            Assert.Equal(140, left.ActualWidth);
            Assert.Equal(36, left.ActualHeight);
            var hit = Assert.IsAssignableFrom<DependencyObject>(host.InputHitTest(new Point(60, 58)));
            Assert.True(ReferenceEquals(left, hit) || left.IsAncestorOf(hit));

            host.UpdatePointer(new Point(60, 58));
            host.Advance(1, animate: false);
            Assert.True(Difference(baseline, Render(host), 18, 58) > 2);
            Assert.False(host.IsRendering);

            host.UpdatePointer(null);
            host.Advance(1, animate: false);
            right.IsEnabled = false;
            window.UpdateLayout();
            var disabled = Render(host);
            host.UpdatePointer(new Point(220, 58));
            host.Advance(1, animate: false);
            Assert.Equal(0, Difference(disabled, Render(host), 178, 58));
            Assert.False(host.IsRendering);
        }
        finally { window.Close(); }
        Pump();
        Assert.False(host.IsRendering);
        Assert.Empty(host.AdornerLayer.GetAdorners(canvas) ?? []);
    });

    [Fact]
    public Task Switching_pages_reuses_the_host_and_releases_the_previous_pages_light_surfaces() => RunSta(() =>
    {
        var first = new Canvas { Background = new SolidColorBrush(Color.FromRgb(32, 33, 39)) };
        AddButton(first, 20, 40);
        var pages = new ContentControl
        {
            Content = first, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var host = new FluentLightHost { Child = pages };
        var window = Show(host);
        try
        {
            var decoration = Assert.Single(host.AdornerLayer.GetAdorners(pages));
            var baseline = Render(host);
            host.UpdatePointer(new Point(60, 58));
            host.Advance(1, animate: false);
            Assert.True(Difference(baseline, Render(host), 18, 58) > 2);

            var second = new Canvas { Background = first.Background };
            AddButton(second, 180, 40);
            pages.Content = second;
            window.UpdateLayout();
            Pump();
            host.UpdatePointer(new Point(220, 58));
            host.Advance(1, animate: false);

            Assert.Same(decoration, Assert.Single(host.AdornerLayer.GetAdorners(pages)));
            Assert.Equal(0, Difference(baseline, Render(host), 18, 58));
            Assert.True(Difference(baseline, Render(host), 178, 58) > 2);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task An_inactive_window_does_not_restart_its_frame_loop_on_pointer_or_resource_changes() => RunSta(() =>
    {
        var canvas = new Canvas();
        AddButton(canvas, 20, 40);
        var host = new FluentLightHost { Child = canvas };
        var window = Show(host);
        try
        {
            Assert.False(window.IsActive);
            host.UpdatePointer(new Point(60, 58));
            host.Resources["TextFillColorPrimaryBrush"] = Brushes.Black;
            Pump();
            Assert.False(host.IsRendering);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Scroll_viewport_clips_lighting_and_reload_keeps_a_single_decoration() => RunSta(() =>
    {
        var canvas = new Canvas { Height = 300, Background = Brushes.Transparent };
        AddButton(canvas, 20, 60);
        var viewer = new ScrollViewer
        {
            Content = canvas, Height = 75, Width = 300, VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(32, 33, 39)) };
        grid.Children.Add(viewer);
        var host = new FluentLightHost { Child = grid };
        var window = Show(host);
        try
        {
            var baseline = Render(host);
            host.UpdatePointer(new Point(60, 72));
            host.Advance(1, animate: false);
            var lit = Render(host);
            Assert.True(Difference(baseline, lit, 20, 65) > 5);
            for (var y = 77; y < 120; y++)
                Assert.Equal(0, Difference(baseline, lit, 20, y));

            viewer.ScrollToVerticalOffset(45);
            window.UpdateLayout();
            host.Advance(1, animate: false);
            Assert.Equal(0, Difference(baseline, Render(host), 18, 33));
            host.UpdatePointer(new Point(60, 33));
            host.Advance(1, animate: false);
            Assert.True(Difference(baseline, Render(host), 20, 20) > 5);
            Assert.True(Difference(baseline, Render(host), 18, 33) > 2);

            window.Content = null;
            Pump();
            Assert.False(host.IsRendering);
            Assert.Empty(host.AdornerLayer.GetAdorners(grid) ?? []);
            window.Content = host;
            window.UpdateLayout();
            Pump();
            Assert.Single(host.AdornerLayer.GetAdorners(grid));
        }
        finally { window.Close(); }
        Assert.False(host.IsRendering);
    });

    private static Button AddButton(Canvas canvas, double x, double y)
    {
        var button = new Button { Content = "Sample action", Width = 140, Height = 36 };
        FluentLight.SetKind(button, FluentLightKind.Button);
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        canvas.Children.Add(button);
        return button;
    }

    private static void WithPointerWash(Action<BitmapSource, BitmapSource, int> check)
    {
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(39, 39, 39)) };
        var surface = new Border { Width = 600, Height = 600 };
        FluentLight.SetKind(surface, FluentLightKind.Surface);
        canvas.Children.Add(surface);
        var host = new FluentLightHost { Child = canvas };
        var window = new Window
        {
            Content = host, Width = 650, Height = 650, WindowStyle = WindowStyle.None,
            Left = -10000, Top = -10000, ShowActivated = false,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Pump();
            var baseline = Render(host);
            host.UpdatePointer(new Point(300.5, 300.5));
            host.Advance(1, animate: false);
            check(baseline, Render(host), 300);
        }
        finally { window.Close(); }
    }

    private static Window Show(FluentLightHost host)
    {
        var window = new Window
        {
            Content = host, Width = 380, Height = 140, WindowStyle = WindowStyle.None, Style = null,
            Left = -10000, Top = -10000, ShowActivated = false,
        };
        window.Show();
        window.UpdateLayout();
        Pump();
        return window;
    }

    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static int Difference(BitmapSource baseline, BitmapSource current, int x, int y)
    {
        var before = new byte[4];
        var after = new byte[4];
        baseline.CopyPixels(new Int32Rect(x, y, 1, 1), before, 4, 0);
        current.CopyPixels(new Int32Rect(x, y, 1, 1), after, 4, 0);
        return Enumerable.Range(0, 3).Sum(i => Math.Abs(before[i] - after[i]));
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

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
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
