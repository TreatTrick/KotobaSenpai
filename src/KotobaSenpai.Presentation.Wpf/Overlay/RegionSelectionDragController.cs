using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Owns mouse capture and pointer feedback for one bounded region drag.</summary>
internal sealed class RegionSelectionDragController(Window owner, Button confirmButton)
{
    private RegionDragMode _mode;
    private Point _start;
    private ScreenRect _initial;

    public bool Begin(Point point, ScreenRect region, double scale)
    {
        _mode = RegionSelectionGeometry.HitTest(region, scale, point.X, point.Y);
        if (_mode == RegionDragMode.None) return false;
        _start = new Point(point.X * scale, point.Y * scale);
        _initial = region;
        if (owner.CaptureMouse())
        {
            UpdateCursor(point, region, scale);
            return true;
        }
        End();
        return false;
    }

    public ScreenRect? Move(Point point, MouseButtonState button, double scale, int width, int height)
    {
        if (_mode == RegionDragMode.None) return null;
        if (button != MouseButtonState.Pressed) { End(); return null; }
        return RegionSelectionGeometry.Drag(_initial, _mode, point.X * scale - _start.X, point.Y * scale - _start.Y, width, height);
    }

    public void CaptureLost() => _mode = RegionDragMode.None;

    public void End()
    {
        _mode = RegionDragMode.None;
        if (owner.IsMouseCaptured) owner.ReleaseMouseCapture();
        owner.Cursor = Cursors.Arrow;
    }

    public void UpdateCursor(Point point, ScreenRect region, double scale)
    {
        if (_mode == RegionDragMode.None && confirmButton.IsMouseOver) { owner.Cursor = Cursors.Hand; return; }
        var mode = _mode == RegionDragMode.None ? RegionSelectionGeometry.HitTest(region, scale, point.X, point.Y) : _mode;
        owner.Cursor = mode switch
        {
            RegionDragMode.Left or RegionDragMode.Right => Cursors.SizeWE,
            RegionDragMode.Top or RegionDragMode.Bottom => Cursors.SizeNS,
            RegionDragMode.TopLeft or RegionDragMode.BottomRight => Cursors.SizeNWSE,
            RegionDragMode.TopRight or RegionDragMode.BottomLeft => Cursors.SizeNESW,
            RegionDragMode.Move => Cursors.SizeAll,
            _ => Cursors.Arrow,
        };
    }
}
