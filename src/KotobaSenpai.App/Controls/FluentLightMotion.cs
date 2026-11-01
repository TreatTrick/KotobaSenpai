using System.Windows;

namespace KotobaSenpai.App.Controls;

/// <summary>Time-based pointer smoothing and fade state that settles exactly when rendering can stop.</summary>
internal sealed class FluentLightMotion
{
    private Point _targetPosition;
    private double _targetPresence;

    internal Point Position { get; private set; }
    internal double Presence { get; private set; }
    internal bool IsSettled => Position == _targetPosition && Presence == _targetPresence;

    internal void SetPointer(Point? position)
    {
        _targetPresence = position.HasValue ? 1 : 0;
        if (position is not { } point) return;
        if (Presence == 0) Position = point;
        _targetPosition = point;
    }

    internal void Advance(double seconds, bool animate)
    {
        var amount = animate ? 1 - Math.Exp(-Math.Max(0, seconds) / 0.032) : 1;
        var next = Position + (_targetPosition - Position) * amount;
        Position = (next - _targetPosition).Length < 0.1 ? _targetPosition : next;
        Presence = Fade(Presence, _targetPresence, seconds, animate);
    }

    internal static double Fade(double current, double target, double seconds, bool animate)
    {
        var next = animate ? current + (target - current) * (1 - Math.Exp(-Math.Max(0, seconds) / 0.07)) : target;
        return Math.Abs(next - target) < 0.002 ? target : next;
    }

    internal static double Proximity(Rect bounds, Point pointer)
    {
        if (bounds.IsEmpty) return 0;
        var dx = Math.Max(Math.Max(bounds.Left - pointer.X, 0), pointer.X - bounds.Right);
        var dy = Math.Max(Math.Max(bounds.Top - pointer.Y, 0), pointer.Y - bounds.Bottom);
        return Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy) / 155);
    }
}
