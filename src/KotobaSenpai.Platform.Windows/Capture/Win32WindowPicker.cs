using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>Picks a top-level window without sending the selection gesture to the source application.</summary>
public sealed class Win32WindowPicker(IStringLocalizer localizer, ILogger logger) : IWindowPicker, IDisposable
{
    private int _active;
    private RainbowWindow? _highlight;
    private nint _highlightTarget;

    public void Highlight(WindowTarget? target) => Highlight(target, Environment.ProcessId);

    internal void Highlight(WindowTarget? target, int excludedProcessId)
    {
        // Keep a retired binding pinned after identity loss until the selection is explicitly reset.
        if (_highlightTarget == (target?.Handle ?? 0)) return;
        _highlight?.Close();
        _highlight = null;
        _highlightTarget = 0;
        if (target is null || ReadCandidate(target.Handle, excludedProcessId) is not { } candidate) return;
        _highlightTarget = target.Handle;
        var outline = new RainbowWindow();
        _highlight = outline;
        outline.Closed += (_, _) => { if (ReferenceEquals(_highlight, outline)) _highlight = null; };
        outline.Follow(candidate, logger);
    }

    public void Dispose() => Highlight(null);

    public async Task<WindowTarget?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("A window picking session is already active.");
        RainbowWindow? outline = null;
        try
        {
            Highlight(null);
            outline = new RainbowWindow(localizer.Get("WindowPicker_Hint"));
            outline.ShowOverDesktop();
            return await new InputSession(outline, logger, cancellationToken).RunAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new WindowsPlatformException(ErrorCodes.WindowSelectionFailed, "Desktop window picking failed.", exception);
        }
        finally
        {
            outline?.Close();
            Volatile.Write(ref _active, 0);
        }
    }

    internal static WindowPickerCandidate? ResolveAt(int x, int y, int excludedProcessId)
    {
        var point = new Native.Point { X = x, Y = y };
        var helpers = OverlayWindowRegistry.Snapshot();
        var hit = Native.GetAncestor(Native.WindowFromPoint(point), 2);
        Native.GetWindowThreadProcessId(hit, out var processId);
        if (hit != 0 && processId != excludedProcessId && !helpers.Contains(hit))
            return ReadCandidate(hit, excludedProcessId);

        WindowPickerCandidate? result = null;
        // Only our own presentation surfaces are skipped; a real external window blocks windows behind it.
        Native.EnumWindows((handle, _) =>
        {
            Native.GetWindowThreadProcessId(handle, out var pid);
            if (pid == excludedProcessId || helpers.Contains(handle) || !Native.IsWindowVisible(handle) || Native.IsIconic(handle)
                || IsCloaked(handle) || !Native.GetWindowRect(handle, out var bounds)) return true;
            if (Native.DwmGetWindowAttribute(handle, 9, out Native.Rect visible, 16) >= 0) bounds = visible;
            if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom) return true;
            result = ReadCandidate(handle, excludedProcessId);
            return false;
        }, 0);
        return result;
    }

    private static bool IsCloaked(nint handle)
        => Native.DwmGetWindowAttribute(handle, 14, out uint cloaked, sizeof(uint)) >= 0 && cloaked != 0;

    internal static WindowPickerCandidate? ReadCandidate(nint handle, int excludedProcessId)
    {
        var threadId = Native.GetWindowThreadProcessId(handle, out var processId);
        if (handle == 0 || processId == excludedProcessId || threadId == 0
            || OverlayWindowRegistry.Snapshot().Contains(handle)
            || !Native.IsWindow(handle) || !Native.IsWindowVisible(handle) || Native.IsIconic(handle)
            || handle == Native.GetDesktopWindow() || handle == Native.GetShellWindow() || IsCloaked(handle)) return null;
        var className = new StringBuilder(256);
        Native.GetClassName(handle, className, className.Capacity);
        if (className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW") return null;
        if (!Native.GetClientRect(handle, out var client) || client.Right <= 0 || client.Bottom <= 0
            || !Native.GetWindowRect(handle, out var frame)) return null;
        var origin = new Native.Point();
        if (!Native.ClientToScreen(handle, ref origin)) return null;
        if (Native.DwmGetWindowAttribute(handle, 9, out Native.Rect visible, 16) >= 0
            && visible.Right > visible.Left && visible.Bottom > visible.Top) frame = visible;
        if (frame.Right <= frame.Left || frame.Bottom <= frame.Top) return null;
        var title = new StringBuilder(Native.GetWindowTextLength(handle) + 1);
        Native.GetWindowText(handle, title, title.Capacity);
        if (Native.GetWindowThreadProcessId(handle, out var latestProcessId) != threadId || latestProcessId != processId)
            return null;
        return new(new WindowTarget(handle, title.ToString(), new ScreenRect(origin.X, origin.Y, client.Right, client.Bottom)),
            new ScreenRect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top),
            processId, threadId, Native.GetDpiForWindow(handle));
    }

    private sealed class InputSession(RainbowWindow outline, ILogger logger, CancellationToken token)
    {
        private readonly TaskCompletionSource<WindowTarget?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WindowPickGesture _gesture = new();
        private Native.HookProc? _mouseCallback;
        private Native.HookProc? _keyboardCallback;
        private Dispatcher? _input;
        private WindowTarget? _selected;
        private Exception? _failure;
        private volatile bool _ending;
        private int _updateQueued;
        private Hover? _hover;

        public Task<WindowTarget?> RunAsync()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Window picker input" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return _completion.Task;
        }

        private void Run()
        {
            nint mouse = 0, keyboard = 0;
            DispatcherTimer? timer = null;
            try
            {
                _input = Dispatcher.CurrentDispatcher;
                using var registration = token.Register(() => _input.BeginInvoke(() => Finish(null)));
                token.ThrowIfCancellationRequested();
                _mouseCallback = OnMouse;
                _keyboardCallback = OnKeyboard;
                var module = Native.GetModuleHandle(null);
                mouse = Native.SetWindowsHookEx(14, _mouseCallback, module, 0);
                if (mouse == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the mouse picking hook.");
                keyboard = Native.SetWindowsHookEx(13, _keyboardCallback, module, 0);
                if (keyboard == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the cancellation key hook.");
                timer = new DispatcherTimer(DispatcherPriority.Input, _input) { Interval = TimeSpan.FromMilliseconds(33) };
                timer.Tick += UpdateHover;
                timer.Start();
                UpdateHover(null, EventArgs.Empty);
                Dispatcher.Run();
            }
            catch (Exception exception) { _failure = exception; }
            finally
            {
                _ending = true;
                timer?.Stop();
                foreach (var hook in new[] { mouse, keyboard })
                    if (hook != 0 && !Native.UnhookWindowsHookEx(hook))
                        logger.LogError(new Win32Exception(Marshal.GetLastWin32Error(), "Unable to release the window picking input hook."),
                            "Failed to release a window picking input hook.");
                if (token.IsCancellationRequested) _completion.TrySetCanceled(token);
                else if (_failure is not null) _completion.TrySetException(_failure);
                else _completion.TrySetResult(_selected);
            }
        }

        private void Finish(WindowTarget? target)
        {
            if (_ending) return;
            _ending = true;
            _selected = target;
            _input!.BeginInvokeShutdown(DispatcherPriority.Background);
        }

        private nint OnMouse(int code, nint message, nint data)
        {
            if (code < 0 || _ending || message != 0x0201 && message != 0x0202)
                return Native.CallNextHookEx(0, code, message, data);
            try
            {
                var point = Marshal.PtrToStructure<Native.Point>(data);
                var candidate = ResolveAt(point.X, point.Y, Environment.ProcessId);
                if (message == 0x0201) _gesture.Press(candidate);
                else
                {
                    var selected = _gesture.Release(candidate);
                    if (_gesture.IsCancellationReady) Finish(null);
                    else if (selected is not null) Finish(selected.Target);
                }
            }
            catch (Exception exception) { _failure = exception; Finish(null); }
            return 1;
        }

        private nint OnKeyboard(int code, nint message, nint data)
        {
            if (code < 0 || _ending || Marshal.ReadInt32(data) != 0x1B)
                return Native.CallNextHookEx(0, code, message, data);
            if (message == 0x0100 || message == 0x0104) _gesture.PressEscape();
            if ((message == 0x0101 || message == 0x0105) && _gesture.ReleaseEscape()) Finish(null);
            return 1;
        }

        private void UpdateHover(object? sender, EventArgs e)
        {
            try
            {
                if (_ending) return;
                if (!Native.GetCursorPos(out var point))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The interactive desktop cursor is unavailable.");
                Volatile.Write(ref _hover, new Hover(ResolveAt(point.X, point.Y, Environment.ProcessId), point.X, point.Y));
                if (Interlocked.Exchange(ref _updateQueued, 1) != 0) return;
                outline.Dispatcher.BeginInvoke(() =>
                {
                    Interlocked.Exchange(ref _updateQueued, 0);
                    try
                    {
                        if (!_ending && Volatile.Read(ref _hover) is { } hover) outline.Update(hover.Candidate, hover.X, hover.Y);
                    }
                    catch (Exception exception)
                    {
                        _input!.BeginInvoke(() => { _failure = exception; Finish(null); });
                    }
                });
            }
            catch (Exception exception) { _failure = exception; Finish(null); }
        }
    }

    private sealed record Hover(WindowPickerCandidate? Candidate, int X, int Y);

    internal sealed class RainbowWindow : DesktopOverlayWindow
    {
        private readonly Canvas _canvas = new();
        private readonly RainbowOutline _outline = new();
        private readonly Border _hint;

        public RainbowWindow(string? hint = null)
        {
            _hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 25, 28, 36)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock { Text = hint ?? string.Empty, Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible,
            };
            _canvas.Children.Add(_outline);
            _canvas.Children.Add(_hint);
            Content = _canvas;
        }

        internal void Follow(WindowPickerCandidate selected, ILogger logger)
        {
            var handle = selected.Target.Handle;
            var desktop = DesktopBounds;
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
            void Refresh(object? sender, EventArgs e)
            {
                try
                {
                    if (Native.GetWindowThreadProcessId(handle, out var processId) != selected.ThreadId || processId != selected.ProcessId)
                    {
                        Close();
                        return;
                    }
                    // Recheck identity even while hidden, so a reused HWND never acquires the old outline.
                    var current = ReadCandidate(handle, 0);
                    if (current is null) { Hide(); return; }
                    if (current.ProcessId != selected.ProcessId || current.ThreadId != selected.ThreadId) { Close(); return; }
                    if (!IsVisible || DesktopBounds != desktop)
                    {
                        desktop = DesktopBounds;
                        ShowOverDesktop();
                    }
                    Update(current, 0, 0);
                }
                catch (Exception exception)
                {
                    Close();
                    logger.LogError(exception, "Failed to follow the selected window outline.");
                }
            }
            timer.Tick += Refresh;
            Closed += (_, _) => { timer.Stop(); timer.Tick -= Refresh; };
            timer.Start();
            Refresh(null, EventArgs.Empty);
        }

        public void Update(WindowPickerCandidate? candidate, int x, int y)
        {
            if (!IsVisible) return;
            _outline.Width = ActualWidth;
            _outline.Height = ActualHeight;
            _outline.Bounds = candidate is not null
                ? FromScreenPixels(new Rect(candidate.FrameBounds.X, candidate.FrameBounds.Y, candidate.FrameBounds.Width, candidate.FrameBounds.Height))
                : Rect.Empty;
            _outline.StrokeScale = (candidate?.Dpi ?? 96) / (96 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            _outline.InvalidateVisual();
            if (_hint.Visibility != Visibility.Visible) return;
            var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            var point = new Native.Point { X = x, Y = y };
            var screen = DesktopBounds;
            if (Native.GetMonitorInfo(Native.MonitorFromPoint(point, 2), ref monitor))
                screen = new ScreenRect(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
            var localScreen = FromScreenPixels(new Rect(screen.X, screen.Y, screen.Width, screen.Height));
            _hint.MaxWidth = Math.Max(1, localScreen.Width - 16);
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var cursor = PointFromScreen(new Point(x, y));
            Canvas.SetLeft(_hint, Math.Clamp(cursor.X + 20, localScreen.Left + 8, Math.Max(localScreen.Left + 8, localScreen.Right - _hint.DesiredSize.Width - 8)));
            Canvas.SetTop(_hint, Math.Clamp(cursor.Y + 24, localScreen.Top + 8, Math.Max(localScreen.Top + 8, localScreen.Bottom - _hint.DesiredSize.Height - 8)));
        }
    }

    private sealed class RainbowOutline : FrameworkElement
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public Rect Bounds { get; set; } = Rect.Empty;
        public double StrokeScale { get; set; } = 1;

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            var perimeter = 2 * (Bounds.Width + Bounds.Height);
            var phase = SystemParameters.ClientAreaAnimation ? _clock.Elapsed.TotalSeconds / 2 : 0;
            var corners = new[] { Bounds.TopLeft, Bounds.TopRight, Bounds.BottomRight, Bounds.BottomLeft, Bounds.TopLeft };
            var offset = 0.0;
            for (var edge = 0; edge < 4; edge++)
            {
                var vector = corners[edge + 1] - corners[edge];
                for (var segment = 0; segment < 40; segment++)
                {
                    var color = RainbowColor((offset + vector.Length * segment / 40) / perimeter + phase);
                    var from = corners[edge] + vector * (segment / 40.0);
                    var to = corners[edge] + vector * ((segment + 1) / 40.0);
                    drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B)), 9 * StrokeScale), from, to);
                    drawingContext.DrawLine(new Pen(new SolidColorBrush(color), 3 * StrokeScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, from, to);
                }
                offset += vector.Length;
            }
        }

        private static Color RainbowColor(double position)
        {
            var hue = (position - Math.Floor(position)) * 6;
            var rising = (byte)(255 * (hue - Math.Floor(hue)));
            var falling = (byte)(255 - rising);
            return (int)hue switch
            {
                0 => Color.FromRgb(255, rising, 0), 1 => Color.FromRgb(falling, 255, 0),
                2 => Color.FromRgb(0, 255, rising), 3 => Color.FromRgb(0, falling, 255),
                4 => Color.FromRgb(rising, 0, 255), _ => Color.FromRgb(255, 0, falling),
            };
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        internal delegate bool EnumProc(nint hwnd, nint state);
        internal delegate nint HookProc(int code, nint message, nint data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint state);
        [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
        [DllImport("user32.dll")] internal static extern nint GetDesktopWindow();
        [DllImport("user32.dll")] internal static extern nint GetShellWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(nint hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out uint value, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    }
}
