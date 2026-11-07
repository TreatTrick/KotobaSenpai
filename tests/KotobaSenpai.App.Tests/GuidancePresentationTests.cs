using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Diagnostics;
using System.IO;
using KotobaSenpai.App.Guidance;
using KotobaSenpai.App.Guidance.Presentation;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Platform.Windows.Overlay;
using KotobaSenpai.Presentation.Wpf.Guidance;
using KotobaSenpai.App.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.App.Tests;

public sealed class GuidancePresentationTests
{
    internal static void CheckGuideLight(bool reactToPointerWhenInactive, string directory)
    {
        var overlay = new GuidanceOverlay((bubble, button) => GuidanceLight.DecorateBubble(bubble, button, reactToPointerWhenInactive));
        overlay.SetText("The card and its action share Fluent lighting.", "I understand");
        var window = new Window { Content = overlay, Width = 640, Height = 360, Style = null,
            WindowStyle = WindowStyle.None, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        FluentLightHost? host = null;
        try
        {
            window.Show();
            overlay.Place(new Rect(420, 290, 120, 36), new Size(640, 360));
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            host = Assert.Single(overlay.Children.OfType<FluentLightHost>());
            var card = Assert.IsType<Border>(host.Child);
            Assert.Equal(FluentLightKind.Surface, FluentLight.GetKind(card));
            Assert.Equal(FluentLightKind.Button, FluentLight.GetKind(overlay.AcknowledgeButton));
            Assert.Single(host.AdornerLayer.GetAdorners(card));
            Assert.False(window.IsActive);
            if (SystemParameters.HighContrast) return;

            var baseline = RenderLight(overlay);
            host.UpdatePointer(new Point(18, 18));
            Assert.Equal(reactToPointerWhenInactive, host.IsRendering);
            host.Advance(1, animate: false);
            Assert.False(baseline.SequenceEqual(RenderLight(overlay)), "The bubble card must show the existing pointer wash.");
            GuidanceDesktopFixture.SavePreview(overlay, directory, $"fluent-card-{reactToPointerWhenInactive}");

            host.UpdatePointer(null);
            host.Advance(1, animate: false);
            baseline = RenderLight(overlay);
            var button = overlay.AcknowledgeButton;
            var point = button.TransformToAncestor(host).Transform(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            host.UpdatePointer(point);
            host.Advance(1, animate: false);
            Assert.False(baseline.SequenceEqual(RenderLight(overlay)), "The acknowledgement must show the existing button glow.");
            var hit = Assert.IsAssignableFrom<DependencyObject>(host.InputHitTest(point));
            Assert.True(ReferenceEquals(button, hit) || button.IsAncestorOf(hit));
            GuidanceDesktopFixture.SavePreview(overlay, directory, $"fluent-button-{reactToPointerWhenInactive}");
            host.UpdatePointer(null);
            Assert.Equal(reactToPointerWhenInactive, host.IsRendering);
            host.Advance(1, animate: false);
            Assert.False(host.IsRendering);
            Assert.True(baseline.SequenceEqual(RenderLight(overlay)), "The light must clear when the pointer leaves.");
            host.UpdatePointer(point);
            host.Visibility = Visibility.Hidden;
            Assert.False(host.IsRendering);
            host.Visibility = Visibility.Visible;
            host.UpdatePointer(point);
            window.WindowState = WindowState.Minimized;
            Assert.False(host.IsRendering);
            Assert.False(window.IsActive);
        }
        finally { window.Close(); }
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Assert.NotNull(host);
        Assert.False(host.IsRendering);
        Assert.Empty(host.AdornerLayer.GetAdorners(host.Child) ?? []);
    }

    private static byte[] RenderLight(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    [Fact]
    public async Task Desktop_guides_support_layout_input_lifecycle_and_localization()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "previews", "guidance");
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "KotobaSenpai.App.Tests.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.Environment[GuidanceDesktopFixture.DirectoryEnvironmentVariable] = directory;
        using var process = Process.Start(start)!;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var failure = Path.Combine(directory, GuidanceDesktopFixture.FailureFileName);
        Assert.True(process.ExitCode == 0, File.Exists(failure) ? File.ReadAllText(failure) : "Guidance desktop fixture failed.");
    }

    internal static void CheckSmallViewport(double width, double height)
    {
        var overlay = new GuidanceOverlay { Width = width, Height = height };
        overlay.SetText("Drag the yellow edges to resize the recognition area.", "I understand");
        var area = new Size(width, height);
        overlay.Place(new Rect(0, 0, 20, 20), area);
        overlay.Measure(area);
        overlay.Arrange(new Rect(area));
        overlay.UpdateLayout();
        Assert.NotNull(Assert.Single(overlay.Children.OfType<Border>(), border => border.Child is Grid).Background);
        Assert.NotNull(overlay.AcknowledgeButton.Background);
        Assert.NotNull(overlay.AcknowledgeButton.Foreground);
        var button = overlay.AcknowledgeButton;
        var bounds = button.TransformToAncestor(overlay).TransformBounds(new Rect(button.RenderSize));
        Assert.True(new Rect(area).Contains(bounds), "The acknowledgement must remain inside a small viewport.");
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
    }

    internal static void CheckMainInput()
    {
        var guidance = new ContextualGuidance(new GuidanceTests.MemorySettings(), new GuidanceTests.TestLogger());
        var localizer = new TestLocalizer();
        var actions = 0;
        var surface = new Button { Content = "Recognize", IsDefault = true, Width = 120, Height = 36 };
        surface.Click += (_, _) => actions++;
        var root = new Grid();
        root.Children.Add(surface);
        var window = new Window { Width = 900, Height = 620, Content = root, Style = null,
            Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            using var presenter = new MainInterfaceTourPresenter(root, surface, _ => surface,
                new TourPresentation(guidance.Main, localizer));
            window.UpdateLayout();
            Assert.False(surface.IsEnabled);
            var overlay = Assert.Single(root.Children.OfType<GuidanceOverlay>());
            var button = overlay.AcknowledgeButton;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.True(overlay.IsKeyboardFocusWithin, "The guide must retain keyboard input without focusing its acknowledgement automatically.");
            Assert.False(button.IsKeyboardFocusWithin, "Opening the guide must not trigger the button's focus halo.");
            Assert.True(overlay.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
            Assert.True(button.IsKeyboardFocusWithin, "Keyboard navigation must still reach the acknowledgement.");
            for (var step = 0; step < 4; step++) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, actions);
            Assert.True(surface.IsEnabled);
            Assert.Empty(root.Children.OfType<Canvas>());
            surface.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, actions);
            Assert.False(guidance.IsActive);
        }
        finally { window.Close(); }
    }

    internal static void CheckRegionLifecycle()
    {
        var settings = new GuidanceTests.MemorySettings();
        var guidance = new ContextualGuidance(settings, new GuidanceTests.TestLogger());
        var localizer = new TestLocalizer();
        var tracker = new Tracker();
        var targetWindow = new Window { Left = -10000, Top = -10000, Width = 800, Height = 450,
            ShowActivated = false, ShowInTaskbar = false, Style = null };
        targetWindow.Show();
        var target = new WindowTarget(new WindowInteropHelper(targetWindow).Handle, "Target", new ScreenRect(-10000, -10000, 800, 450));
        tracker.Publish(new(target.Handle, target.Title, target.Bounds, 1, true, false, true));
        var selector = new RegionSelectorWindow(settings, localizer, tracker, new TourPresentation(guidance.Region, localizer),
            (bubble, button) => GuidanceLight.DecorateBubble(bubble, button, reactToPointerWhenInactive: true));
        try
        {
            selector.Flash(target);
            Assert.Null(guidance.Region.Current);
            Assert.Empty(((Canvas)selector.Content).Children.OfType<GuidanceOverlay>());
            selector.StopFlashing();
            selector.Show(target);
            var canvas = Assert.IsType<Canvas>(selector.Content);
            Assert.Single(canvas.Children.OfType<Canvas>());
            var confirm = Assert.Single(canvas.Children.OfType<Button>());
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(selector.IsVisible);
            Assert.Null(settings.GetValue(RecognitionRegion.SettingsKey));
            guidance.Region.Advance();
            tracker.Publish(tracker.Current! with { IsValid = false });
            Assert.Null(guidance.Region.Current);
            Assert.Null(settings.GetValue(ContextualGuidance.RegionCompletionKey));
            tracker.Publish(tracker.Current! with { IsValid = true });
            selector.Show(target);
            Assert.Equal(GuidanceTarget.RegionTopLeft, guidance.Region.Current?.Target);
            guidance.Region.Advance();
            tracker.Publish(tracker.Current! with { IsMinimized = true });
            Assert.False(selector.IsVisible);
            Assert.Equal(GuidanceTarget.RegionConfirm, guidance.Region.Current?.Target);
            tracker.Publish(tracker.Current! with { IsMinimized = false });
            Assert.True(selector.IsVisible);
            Assert.Single(canvas.Children.OfType<Canvas>());
            Assert.Equal(GuidanceTarget.RegionConfirm, guidance.Region.Current?.Target);
            localizer.Refresh();
            Assert.Equal(GuidanceTarget.RegionConfirm, guidance.Region.Current?.Target);
            guidance.Region.Advance();
            Assert.Empty(canvas.Children.OfType<Canvas>());
            Assert.True(selector.IsVisible);
            Assert.Equal(ContextualGuidance.CompletedValue, settings.GetValue(ContextualGuidance.RegionCompletionKey));
            Assert.Null(settings.GetValue(RecognitionRegion.SettingsKey));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(selector.IsVisible);
            Assert.NotNull(settings.GetValue(RecognitionRegion.SettingsKey));
            selector.Show(target);
            Assert.Empty(canvas.Children.OfType<Canvas>());
        }
        finally { selector.Close(); targetWindow.Close(); }
    }

    [Fact]
    public void Localized_adapter_refreshes_without_changing_steps_and_detaches_on_disposal()
    {
        var guidance = new ContextualGuidance(new GuidanceTests.MemorySettings(), new GuidanceTests.TestLogger());
        var localizer = new TestLocalizer();
        var presentation = new TourPresentation(guidance.Main, localizer);
        var changes = 0;
        presentation.Changed += (_, _) => changes++;
        presentation.Start();
        var target = presentation.Target;
        localizer.Refresh();
        Assert.Equal(target, presentation.Target);
        Assert.Equal($"Updated {ResourceKeys.Guide_SelectWindow}", presentation.Text);
        Assert.Equal(2, changes);
        presentation.Dispose();
        localizer.Refresh();
        Assert.Equal(2, changes);
        Assert.False(guidance.IsActive);
    }

    private sealed class TestLocalizer : IStringLocalizer
    {
        private string _prefix = string.Empty;
        public string Get(string key, params object[] args) => _prefix + key;
        public event EventHandler? CultureChanged;
        public void Refresh() { _prefix = "Updated "; CultureChanged?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class Tracker : ITargetWindowTracker
    {
        public TargetWindowSnapshot? Current { get; private set; }
        public event EventHandler<TargetWindowSnapshot>? Changed;
        public void Publish(TargetWindowSnapshot snapshot) { Current = snapshot; Changed?.Invoke(this, snapshot); }
        public TargetWindowSnapshot Attach(WindowTarget target) => Current!;
        public TargetWindowSnapshot Refresh() => Current!;
        public void Detach() { }
        public void Dispose() { }
    }
}
