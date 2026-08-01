namespace KotobaSenpai.Core.Models;

/// <summary>覆盖层在屏幕上绘制的下划线几何，使用物理像素坐标。</summary>
public readonly record struct OverlayLine
{
    public OverlayLine(int x, int y, int width, int thickness = 2)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (thickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(thickness));

        X = x;
        Y = y;
        Width = width;
        Thickness = thickness;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Thickness { get; }
}
