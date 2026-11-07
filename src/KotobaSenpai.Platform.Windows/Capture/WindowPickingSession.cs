using System.ComponentModel;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Platform.Windows.Capture;

internal sealed record WindowPickHover(WindowPickerCandidate? Candidate, int X, int Y);

/// <summary>Owns the native picking thread, hooks and message pump; presentation receives immutable hover observations.</summary>
internal sealed class WindowPickingSession(ILogger logger, CancellationToken token)
{
    private readonly TaskCompletionSource<WindowTarget?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowPickGesture _gesture = new();
    private WindowPickerNativeMethods.HookProc? _mouseCallback;
    private WindowPickerNativeMethods.HookProc? _keyboardCallback;
    private uint _threadId;
    private WindowTarget? _selected;
    private Exception? _failure;
    private volatile bool _ending;

    public event EventHandler<WindowPickHover>? HoverChanged;
    public bool IsActive => !_ending && !_completion.Task.IsCompleted;

    public Task<WindowTarget?> RunAsync()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Window picker input" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return _completion.Task;
    }

    public void Fail(Exception exception)
    {
        if (!IsActive) return;
        Interlocked.CompareExchange(ref _failure, exception, null);
        StopFromAnotherThread();
    }

    private void Run()
    {
        nint mouse = 0, keyboard = 0;
        nuint timer = 0;
        try
        {
            _threadId = WindowPickerNativeMethods.GetCurrentThreadId();
            // Create the queue before cancellation can post its quit message.
            WindowPickerNativeMethods.PeekMessage(out _, 0, 0, 0, 0);
            using var registration = token.Register(StopFromAnotherThread);
            token.ThrowIfCancellationRequested();
            if (_ending) return;
            _mouseCallback = OnMouse;
            _keyboardCallback = OnKeyboard;
            var module = WindowPickerNativeMethods.GetModuleHandle(null);
            mouse = WindowPickerNativeMethods.SetWindowsHookEx(14, _mouseCallback, module, 0);
            if (mouse == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the mouse picking hook.");
            keyboard = WindowPickerNativeMethods.SetWindowsHookEx(13, _keyboardCallback, module, 0);
            if (keyboard == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the cancellation key hook.");
            timer = WindowPickerNativeMethods.SetTimer(0, 0, 33, 0);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start window picking hover updates.");
            UpdateHover();
            while (true)
            {
                var result = WindowPickerNativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "The window picking message pump failed.");
                if (result == 0) break;
                if (message.Message == 0x0113 && message.WParam == timer) UpdateHover();
            }
        }
        catch (Exception exception) { Interlocked.CompareExchange(ref _failure, exception, null); }
        finally
        {
            _ending = true;
            if (timer != 0) WindowPickerNativeMethods.KillTimer(0, timer);
            foreach (var hook in new[] { mouse, keyboard })
                if (hook != 0 && !WindowPickerNativeMethods.UnhookWindowsHookEx(hook))
                    logger.LogError(new Win32Exception(Marshal.GetLastWin32Error(), "Unable to release the window picking input hook."),
                        "Failed to release a window picking input hook.");
            GC.KeepAlive(_mouseCallback);
            GC.KeepAlive(_keyboardCallback);
            if (token.IsCancellationRequested) _completion.TrySetCanceled(token);
            else if (_failure is not null) _completion.TrySetException(_failure);
            else _completion.TrySetResult(_selected);
        }
    }

    private void StopFromAnotherThread()
    {
        if (_completion.Task.IsCompleted) return;
        _ending = true;
        WindowPickerNativeMethods.PostThreadMessage(_threadId, 0x0012, 0, 0);
    }

    private void Finish(WindowTarget? target)
    {
        if (_ending) return;
        _ending = true;
        _selected = target;
        WindowPickerNativeMethods.PostQuitMessage(0);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code < 0 || _ending || message != 0x0201 && message != 0x0202)
            return WindowPickerNativeMethods.CallNextHookEx(0, code, message, data);
        try
        {
            var point = Marshal.PtrToStructure<WindowPickerNativeMethods.Point>(data);
            var candidate = WindowPickerCandidates.ResolveAt(point.X, point.Y, Environment.ProcessId);
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
            return WindowPickerNativeMethods.CallNextHookEx(0, code, message, data);
        if (message == 0x0100 || message == 0x0104) _gesture.PressEscape();
        if ((message == 0x0101 || message == 0x0105) && _gesture.ReleaseEscape()) Finish(null);
        return 1;
    }

    private void UpdateHover()
    {
        try
        {
            if (_ending) return;
            if (!WindowPickerNativeMethods.GetCursorPos(out var point))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The interactive desktop cursor is unavailable.");
            HoverChanged?.Invoke(this, new WindowPickHover(WindowPickerCandidates.ResolveAt(point.X, point.Y, Environment.ProcessId), point.X, point.Y));
        }
        catch (Exception exception) { _failure = exception; Finish(null); }
    }
}
