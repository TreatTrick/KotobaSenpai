using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Platform.Windows.Capture;

internal sealed record WindowPickerCandidate(
    WindowTarget Target, ScreenRect FrameBounds, uint ProcessId, uint ThreadId, uint Dpi);

/// <summary>Consumes a complete click before committing a candidate.</summary>
internal sealed class WindowPickGesture
{
    private WindowPickerCandidate? _pressed;
    private bool _buttonPressed;
    private bool _cancelled;
    private bool _escapePressed;

    public bool IsCancellationReady => _cancelled && !_buttonPressed && !_escapePressed;

    public void PressEscape()
    {
        _escapePressed = true;
        _cancelled = true;
    }

    public bool ReleaseEscape()
    {
        _escapePressed = false;
        return IsCancellationReady;
    }

    public void Press(WindowPickerCandidate? candidate)
    {
        _buttonPressed = true;
        _pressed = candidate;
    }

    public WindowPickerCandidate? Release(WindowPickerCandidate? candidate)
    {
        var pressed = _pressed;
        _pressed = null;
        _buttonPressed = false;
        return !_cancelled && pressed is not null && candidate is not null
            && pressed.Target.Handle == candidate.Target.Handle
            && pressed.ProcessId == candidate.ProcessId && pressed.ThreadId == candidate.ThreadId
                ? candidate : null;
    }
}
