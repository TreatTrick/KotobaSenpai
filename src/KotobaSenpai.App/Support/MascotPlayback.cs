using System.Windows;

namespace KotobaSenpai.App.Support;

internal enum MascotPhase { FollowingMouse, CheeringUp, Celebrating }

/// <summary>Elapsed-time playback and mouse-distance filtering, independent of the rendering subscription.</summary>
internal sealed class MascotPlayback
{
    internal const int FrameCount = 121;
    private double _cursor;
    private Point? _anchor;
    private double _time;
    public MascotPhase Phase { get; private set; }
    public int Direction { get; set; } = -1;
    public string Clip => Phase == MascotPhase.Celebrating ? "B" : "A";
    public int Frame => (int)_cursor;
    public double Breath => Phase == MascotPhase.FollowingMouse ? 1 + Math.Sin(_time * 2.2) * 0.006 : 1;

    public void Reset(bool supported)
    {
        Phase = supported ? MascotPhase.Celebrating : MascotPhase.FollowingMouse;
        _cursor = 0;
        _anchor = null;
        Direction = -1;
    }

    public void Celebrate()
    {
        if (Phase == MascotPhase.FollowingMouse) Phase = MascotPhase.CheeringUp;
    }

    public void ResetMouseAnchor() => _anchor = null;

    public void FollowPopup(Point point, Rect closeButton, Rect window)
    {
        var corner = new Rect(window.Right - window.Width / 4, window.Top, window.Width / 4, window.Height / 4);
        if (corner.Contains(point)) Follow(point, closeButton, false);
        else { Direction = 1; _anchor = point; }
    }

    public void Follow(Point point, Rect target, bool happyWhenApproaching)
    {
        // Compare both cursor positions with the current target so moving a window alone has no effect.
        if (_anchor == point) return;
        if (target.Contains(point))
        {
            Direction = happyWhenApproaching ? 1 : -1;
            _anchor = point;
            return;
        }
        if (_anchor is not { } anchor) { _anchor = point; return; }
        var change = Distance(point, target) - Distance(anchor, target);
        if (Math.Abs(change) < 3) return;
        Direction = (change < 0) == happyWhenApproaching ? 1 : -1;
        _anchor = point;
    }

    public void Advance(double elapsed)
    {
        var delta = Math.Clamp(elapsed, 0, 0.1);
        _time += delta;
        var step = delta * 24;
        switch (Phase)
        {
            case MascotPhase.FollowingMouse:
                _cursor = Math.Clamp(_cursor + Direction * step, 0, FrameCount - 1);
                break;
            case MascotPhase.CheeringUp:
                _cursor += step;
                if (_cursor >= FrameCount - 1) { Phase = MascotPhase.Celebrating; _cursor = 0; }
                break;
            case MascotPhase.Celebrating:
                _cursor = (_cursor + step) % FrameCount;
                break;
        }
    }

    private static double Distance(Point point, Rect rect)
    {
        var dx = Math.Max(0, Math.Max(rect.Left - point.X, point.X - rect.Right));
        var dy = Math.Max(0, Math.Max(rect.Top - point.Y, point.Y - rect.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
