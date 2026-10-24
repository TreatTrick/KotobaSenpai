using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Platform.Windows.Overlay;

internal sealed class PhrasePopupHoverTracker
{
    private const double ExitMarginDip = 10;
    private bool _hasEntered;
    private double? _previousDistanceSquared;

    public void MarkEntered() => _hasEntered = true;

    public void Reset()
    {
        _hasEntered = false;
        _previousDistanceSquared = null;
    }

    public bool ShouldRemainOpen(ScreenRect bounds, int x, int y, double dpiScale, bool isOverSource)
    {
        var dx = x - Math.Clamp((double)x, bounds.X, bounds.Right);
        var dy = y - Math.Clamp((double)y, bounds.Y, bounds.Bottom);
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared == 0)
            MarkEntered();

        var margin = ExitMarginDip * dpiScale;
        var keep = _hasEntered
            ? distanceSquared <= margin * margin
            : isOverSource || _previousDistanceSquared is null || distanceSquared <= _previousDistanceSquared.Value;
        _previousDistanceSquared = distanceSquared;
        return keep;
    }
}
