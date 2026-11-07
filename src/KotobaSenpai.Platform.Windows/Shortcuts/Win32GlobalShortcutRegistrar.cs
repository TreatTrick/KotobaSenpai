using System.Runtime.InteropServices;
using KotobaSenpai.Core.Threading;
using KotobaSenpai.Core.Shortcuts;

namespace KotobaSenpai.Platform.Windows.Shortcuts;

/// <summary>Owns native registrations and a message-only window; knows nothing about reading actions.</summary>
public sealed class Win32GlobalShortcutRegistrar : IGlobalShortcutRegistrar
{
    private readonly IUiThread _uiThread;
    private HotkeyMessageWindow? _source;
    private readonly HashSet<int> _registrations = [];
    private bool _disposed;

    public Win32GlobalShortcutRegistrar(IUiThread uiThread)
        => _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));

    public event EventHandler<int>? Triggered;

    public bool TryRegister(int registrationId, ShortcutGesture gesture, out int error)
    {
        var nativeError = 0;
        var registered = _uiThread.Invoke(() => TryRegisterCore(registrationId, gesture, out nativeError));
        error = nativeError;
        return registered;
    }

    private bool TryRegisterCore(int registrationId, ShortcutGesture gesture, out int error)
    {
        error = 87;
        if (_disposed || registrationId is <= 0 or > 0xBFFF || _registrations.Contains(registrationId)
            || !ShortcutGesture.TryParse(gesture.ToString(), out var parsed) || parsed != gesture) return false;
        _source ??= new HotkeyMessageWindow(OnHotkey);
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
        => _uiThread.Invoke(() => UnregisterCore(registrationId));

    private void UnregisterCore(int registrationId)
    {
        if (_registrations.Remove(registrationId) && _source is not null) Native.UnregisterHotKey(_source.Handle, registrationId);
    }

    private void OnHotkey(int id)
    {
        _uiThread.Post(() =>
        {
            if (!_disposed && _registrations.Contains(id)) Triggered?.Invoke(this, id);
        });
    }

    public void Dispose()
        => _uiThread.Invoke(DisposeCore);

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _registrations.ToArray()) UnregisterCore(id);
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
