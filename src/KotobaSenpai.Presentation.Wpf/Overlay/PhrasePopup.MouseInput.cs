using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

public sealed partial class PhrasePopup
{
    private readonly Native.MouseHook _mouseInputCallback;
    private nint _mouseInputHook;
    private bool _translationMouseDown;
    private int _translationVersion;
    private int _pressedTranslationVersion;

    private void UpdateMouseInputHook()
    {
        // Keep a swallowed press paired with its release, even if the popup is hidden meanwhile.
        if (!IsVisible && !_translationMouseDown)
        {
            ReleaseMouseInputHook();
            return;
        }
        if (_mouseInputHook != 0) return;
        _mouseInputHook = Native.SetWindowsHookEx(14, _mouseInputCallback, Native.GetModuleHandle(null), 0);
        if (_mouseInputHook == 0)
            _logger?.LogWarning("[OVERLAY] Popup mouse input hook could not be installed: error={0}.", Marshal.GetLastWin32Error());
    }

    private void ReleaseMouseInputHook()
    {
        if (_mouseInputHook == 0) return;
        Native.UnhookWindowsHookEx(_mouseInputHook);
        _mouseInputHook = 0;
    }

    private nint InterceptMouseInput(int code, nint message, nint data)
    {
        var kind = (int)message;
        if (code >= 0 && kind is Native.WmLeftButtonDown or Native.WmLeftButtonUp or Native.WmMouseWheel)
        {
            var input = Marshal.PtrToStructure<Native.MouseHookData>(data);
            var overPopup = IsVisible && Native.WindowFromPoint(input.Point) == new WindowInteropHelper(this).Handle;
            var overTranslation = overPopup && TranslationButton.IsVisible
                && TranslationButton.InputHitTest(TranslationButton.PointFromScreen(new Point(input.Point.X, input.Point.Y))) is not null;
            if (HandleMouseInput(kind, overPopup, overTranslation, unchecked((short)(input.MouseData >> 16))))
                return 1;
        }
        return Native.CallNextHookEx(0, code, message, data);
    }

    internal bool HandleMouseInput(int message, bool isOverPopup, bool isOverTranslation, int wheelDelta = 0)
    {
        if (message == Native.WmLeftButtonUp && _translationMouseDown)
        {
            _translationMouseDown = false;
            TranslationButton.Opacity = 1;
            var version = _pressedTranslationVersion;
            if (IsVisible && isOverPopup && isOverTranslation && version == _translationVersion)
            {
                // The physical click is suppressed before raw input and async button state reach the game.
                // Dispatch the local action instead of injecting another system mouse event.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!IsVisible || !TranslationButton.IsVisible || version != _translationVersion) return;
                    TranslationButton.IsChecked = TranslationButton.IsChecked != true;
                    TranslationButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }));
            }
            UpdateMouseInputHook();
            return true;
        }
        if (!IsVisible || !isOverPopup) return false;
        if (message == Native.WmLeftButtonDown && isOverTranslation && TranslationButton.IsVisible && TranslationButton.IsEnabled)
        {
            _translationMouseDown = true;
            _pressedTranslationVersion = _translationVersion;
            TranslationButton.Opacity = 0.6;
            return true;
        }
        if (message != Native.WmMouseWheel) return false;
        var wheelVersion = _translationVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsVisible && wheelVersion == _translationVersion) ScrollPopup(wheelDelta);
        }));
        return true;
    }

    private void ScrollPopup(int delta)
        => PopupScrollViewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
        });

    private static partial class Native
    {
        public const int WmLeftButtonDown = 0x0201;
        public const int WmLeftButtonUp = 0x0202;
        public delegate nint MouseHook(int code, nint message, nint data);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseHookData
        {
            public Point Point;
            public uint MouseData, Flags, Time;
            public nuint ExtraInfo;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        public static extern nint SetWindowsHookEx(int kind, MouseHook callback, nint module, uint thread);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hook);

        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

        [DllImport("user32.dll")]
        public static extern nint WindowFromPoint(Point point);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
        public static extern nint GetModuleHandle(string? name);
    }
}
