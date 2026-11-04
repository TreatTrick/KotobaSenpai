using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using KotobaSenpai.Core.Shortcuts;

namespace KotobaSenpai.Platform.Windows.Shortcuts;

/// <summary>Owns native registrations and a message-only window; knows nothing about reading actions.</summary>
public sealed class Win32GlobalShortcutRegistrar : IGlobalShortcutRegistrar
{
    private HwndSource? _source;
    private readonly HashSet<int> _registrations = [];
    private bool _disposed;

    private HwndSource CreateSource()
    {
        var source = new HwndSource(new HwndSourceParameters("KotobaSenpai shortcuts")
        { ParentWindow = new nint(-3), WindowStyle = 0, Width = 0, Height = 0 });
        source.AddHook(WindowProc);
        return source;
    }

    public event EventHandler<int>? Triggered;

    public bool TryRegister(int registrationId, ShortcutGesture gesture, out int error)
    {
        error = 87;
        if (_disposed || registrationId is <= 0 or > 0xBFFF || _registrations.Contains(registrationId)
            || !ShortcutGesture.TryParse(gesture.ToString(), out var parsed) || parsed != gesture) return false;
        _source ??= CreateSource();
        var key = parsed.Key;
        var virtualKey = key.Length == 1 ? key[0]
            : key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var number) ? 0x70 + number - 1
            : key switch
            {
                "Backspace" => 0x08, "Tab" => 0x09, "Enter" => 0x0D, "Escape" => 0x1B,
                "Space" => 0x20, "PageUp" => 0x21, "PageDown" => 0x22, "End" => 0x23,
                "Home" => 0x24, "Left" => 0x25, "Up" => 0x26, "Right" => 0x27,
                "Down" => 0x28, "Insert" => 0x2D, "Delete" => 0x2E, _ => 0,
            };
        if (virtualKey == 0 || !Native.RegisterHotKey(_source.Handle, registrationId, (uint)parsed.Modifiers | 0x4000, (uint)virtualKey))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }
        _registrations.Add(registrationId);
        error = 0;
        return true;
    }

    public void Unregister(int registrationId)
    {
        if (_registrations.Remove(registrationId) && _source is not null) Native.UnregisterHotKey(_source.Handle, registrationId);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            var id = wParam.ToInt32();
            handled = true;
            _source!.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && _registrations.Contains(id)) Triggered?.Invoke(this, id);
            }, DispatcherPriority.Input);
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _registrations.ToArray()) Unregister(id);
        _source?.RemoveHook(WindowProc);
        _source?.Dispose();
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(nint window, int id);
    }
}
