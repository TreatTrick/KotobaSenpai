using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KotobaSenpai.App.Controls;
using KotobaSenpai.App.Reading;
using KotobaSenpai.App.Shortcuts;
using KotobaSenpai.App.Views;
using KotobaSenpai.Core.Shortcuts;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Platform.Windows.Overlay;
using KotobaSenpai.Platform.Windows.Shortcuts;
using Wpf.Ui.Appearance;
using KotobaSenpai.Presentation.Wpf.Overlay;
using KotobaSenpai.Presentation.Wpf.Threading;

namespace KotobaSenpai.App.Tests;

public sealed partial class PhrasePopupThemeTests
{
    private static void CheckShortcutSettingsPresentation(SettingsPage page, FrameworkElement frame,
        ReadingShortcutCoordinator shortcuts, string culture, ApplicationTheme theme)
    {
        var list = Assert.IsType<ItemsControl>(page.FindName("KeyboardShortcutsList"));
        var scroll = Assert.IsType<ScrollViewer>(page.FindName("SettingsScrollViewer"));
        scroll.ScrollToVerticalOffset(list.TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 50);
        frame.UpdateLayout();
        var buttons = Descendants(list).OfType<ShortcutCaptureButton>().ToArray();
        Assert.Equal(5, buttons.Length);
        Assert.All(buttons, button =>
        {
            Assert.Equal(FluentLightKind.Button, FluentLight.GetKind(button));
            Assert.Equal(4, FluentLight.GetCornerRadius(button));
            Assert.True(button.ActualWidth >= 180);
            Assert.True(button.TranslatePoint(new Point(button.ActualWidth, 0), page).X <= page.ActualWidth);
        });
        var capture = buttons[0];
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(new ButtonAutomationPeer(capture).GetPattern(PatternInterface.Invoke));
        invoke.Invoke();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Input);
        Assert.True(shortcuts.IsRecording, $"Recording did not start for {culture}/{theme}: focused={capture.IsKeyboardFocused}, visible={capture.IsVisible}, enabled={capture.IsEnabled}.");
        capture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(capture), 0, Key.F6)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        capture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(capture), 0, Key.F6)
            { RoutedEvent = Keyboard.PreviewKeyUpEvent });
        Assert.False(shortcuts.IsRecording);
        Assert.Equal("F6", capture.Entry!.Hint);
        Assert.True(ShortcutGesture.TryParse("Ctrl+Alt+Shift+F11", out var combination));
        Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, combination).Succeeded);
        frame.UpdateLayout();
        Assert.Equal(combination.ToString(), capture.Content);
        GuidanceDesktopFixture.SavePreview(frame, Path.Combine(AppContext.BaseDirectory, "previews"), $"shortcuts-{culture}-{theme}");
        capture.Entry.ResetCommand.Execute(null);
        Assert.Equal("F1", capture.Entry.Hint);
    }

    private static void CheckNativeShortcuts()
    {
        using var registrar = new Win32GlobalShortcutRegistrar(new WpfUiThread(Dispatcher.CurrentDispatcher));
        using var shortcuts = new ReadingShortcutCoordinator(new MemorySettings(), registrar);
        var actions = new NativeReadingActions();
        shortcuts.Attach(actions);
        shortcuts.Start();
        var shell = new Window { Title = "Shortcut lifecycle test", ShowActivated = false, ShowInTaskbar = false,
            Style = null, AllowsTransparency = false,
            WindowState = WindowState.Minimized, Left = -10000, Top = -10000, Width = 200, Height = 100 };
        shell.Show();
        try
        {
            foreach (var action in Enum.GetValues<ReadingAction>())
            {
                Assert.NotNull(shortcuts.GetBinding(action).RegistrationId);
                PressFunctionKey((byte)(0x70 + (int)action));
            }
            Assert.Equal(Enum.GetValues<ReadingAction>(), actions.Executed);
            Assert.Equal(WindowState.Minimized, shell.WindowState);
            ShortcutNative.AssertNoFocusStealing(new WindowInteropHelper(shell).Handle);

            var previous = actions.Executed.Count;
            PressFunctionKey(0x71, repeat: true);
            Assert.Equal(previous + 1, actions.Executed.Count);
            CheckExternalConflict(shortcuts, actions);
            using var contender = new Win32GlobalShortcutRegistrar(new WpfUiThread(Dispatcher.CurrentDispatcher));
            Assert.False(contender.TryRegister(100, new("F1"), out var conflict));
            Assert.Equal(1409, conflict);
            Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, new("F6")).Succeeded);
            PressFunctionKey(0x75);
            Assert.Equal(ReadingAction.Recognize, actions.Executed[^1]);
            Assert.True(contender.TryRegister(101, new("F1"), out _));
            contender.Unregister(101);
            Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, null).Succeeded);
            Assert.True(contender.TryRegister(102, new("F6"), out _));
            shortcuts.Dispose();
            foreach (var action in Enum.GetValues<ReadingAction>())
                Assert.True(contender.TryRegister(200 + (int)action, ReadingShortcutCoordinator.DefaultGesture(action), out _));
        }
        finally { shell.Close(); }
    }

    private static void CheckSuspendedOverlay()
    {
        var target = new Window { Style = null, AllowsTransparency = false, ShowActivated = false, ShowInTaskbar = false,
            Left = -10000, Top = -10000, Width = 400, Height = 200 };
        target.Show();
        var tracker = new PresentationTracker();
        tracker.Attach(new(new WindowInteropHelper(target).Handle, "Overlay test target", new(-10000, -10000, 400, 200)));
        var settings = new MemorySettings();
        var renderer = new WpfOverlayRenderer(settings: settings, tracker: tracker);
        var session = WordOverlaySession.Start(tracker.Current!.Target, []);
        Window? overlay = null;
        try
        {
            renderer.Show(session);
            overlay = Assert.Single(Application.Current.Windows.Cast<Window>(), window => window.GetType().Name == "OverlayWindow");
            Assert.True(overlay.IsVisible);
            renderer.Suspend();
            Assert.False(overlay.IsVisible);
            tracker.Publish(tracker.Current! with { IsMinimized = true });
            tracker.Publish(tracker.Current! with { IsMinimized = false, Bounds = new(-9900, -9900, 500, 250) });
            settings.SetValue(KotobaSenpai.Core.Settings.OverlayAppearanceSettings.PopupBackgroundTransparencyKey, "0.6");
            renderer.RefreshAppearance();
            renderer.Show(session, () => false);
            Assert.False(overlay.IsVisible);
            renderer.Show(session);
            Assert.True(overlay.IsVisible);
            renderer.Hide();
            Assert.False(overlay.IsVisible);
        }
        finally { overlay?.Close(); target.Close(); }
    }

    private sealed class PresentationTracker : ITargetWindowTracker
    {
        public event EventHandler<TargetWindowSnapshot>? Changed;
        public TargetWindowSnapshot? Current { get; private set; }
        public TargetWindowSnapshot Attach(WindowTarget target)
            => Current = new(target.Handle, target.Title, target.Bounds, 1, true, false, true);
        public void Publish(TargetWindowSnapshot state) { Current = state; Changed?.Invoke(this, state); }
        public TargetWindowSnapshot Refresh() => Current!;
        public void Detach() => Current = null;
        public void Dispose() => Detach();
    }

    private static void CheckExternalConflict(ReadingShortcutCoordinator shortcuts, NativeReadingActions actions)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "KotobaSenpai.App.Tests.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        start.Environment["KOTOBA_SHORTCUT_CONFLICT_FIXTURE"] = "1";
        using var holder = Process.Start(start)!;
        try
        {
            Assert.Equal("ready", holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(ShortcutGesture.TryParse("Ctrl+Alt+Shift+F11", out var gesture));
            var previous = shortcuts.GetBinding(ReadingAction.Recognize);
            var result = shortcuts.ChangeBinding(ReadingAction.Recognize, gesture);
            Assert.Equal(ShortcutBindingError.Unavailable, result.Error);
            Assert.Equal(1409, result.NativeError);
            Assert.Equal(previous, shortcuts.GetBinding(ReadingAction.Recognize));
            var settings = new MemorySettings();
            settings.SetValue(ReadingShortcutCoordinator.SettingsKey(ReadingAction.Recognize), gesture.ToString());
            using (var registrar = new Win32GlobalShortcutRegistrar(new WpfUiThread(Dispatcher.CurrentDispatcher)))
            using (var startup = new ReadingShortcutCoordinator(settings, registrar))
            {
                startup.Start();
                Assert.Equal(ShortcutBindingError.Unavailable, startup.GetBinding(ReadingAction.Recognize).Error);
            }
            holder.StandardInput.WriteLine("stop");
            Assert.True(holder.WaitForExit(5000));
            Assert.Equal(0, holder.ExitCode);
            Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, gesture).Succeeded);
            var before = actions.Executed.Count;
            try
            {
                foreach (var key in new byte[] { 0x11, 0x12, 0x10 }) ShortcutNative.keybd_event(key, 0, 0, UIntPtr.Zero);
                PressFunctionKey(0x7A);
            }
            finally
            {
                foreach (var key in new byte[] { 0x10, 0x12, 0x11 }) ShortcutNative.keybd_event(key, 0, 2, UIntPtr.Zero);
            }
            Assert.Equal(before + 1, actions.Executed.Count);
            Assert.Equal(ReadingAction.Recognize, actions.Executed[^1]);
            Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, new("F1")).Succeeded);
        }
        finally { if (!holder.HasExited) holder.Kill(entireProcessTree: true); }
    }

    private static void PressFunctionKey(byte key, bool repeat = false)
    {
        try
        {
            ShortcutNative.keybd_event(key, 0, 0, UIntPtr.Zero);
            if (repeat) ShortcutNative.keybd_event(key, 0, 0, UIntPtr.Zero);
        }
        finally { ShortcutNative.keybd_event(key, 0, 2, UIntPtr.Zero); }
        var settled = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); settled.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(settled);
    }

    private sealed class NativeReadingActions : IReadingActions
    {
        public List<ReadingAction> Executed { get; } = [];
        public bool CanExecute(ReadingAction action) => true;
        public Task ExecuteAsync(ReadingAction action) { Executed.Add(action); return Task.CompletedTask; }
    }

    private sealed class PreviewShortcutRegistrar : IGlobalShortcutRegistrar
    {
        public event EventHandler<int>? Triggered { add { } remove { } }
        public bool TryRegister(int registrationId, ShortcutGesture gesture, out int error) { error = 0; return true; }
        public void Unregister(int registrationId) { }
        public void Dispose() { }
    }

    private static class ShortcutNative
    {
        [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

        /// <summary>
        /// Asserts that handling the global shortcuts did not activate any window owned by this process.
        /// Comparing the raw foreground handle before and after the key presses is not usable here: any
        /// unrelated desktop activity (a browser, another application, the CI agent) can legitimately change
        /// the foreground window mid-test without the application stealing focus, which produced false
        /// negatives on developer machines and would make the test unusable as a merge gate.
        /// </summary>
        internal static void AssertNoFocusStealing(nint shell)
        {
            var foreground = GetForegroundWindow();
            Assert.NotEqual(shell, foreground);
            if (foreground == 0)
                return;
            GetWindowThreadProcessId(foreground, out var owner);
            Assert.NotEqual((uint)Environment.ProcessId, owner);
        }
    }
}
