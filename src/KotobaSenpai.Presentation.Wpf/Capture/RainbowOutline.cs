using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace KotobaSenpai.Presentation.Wpf.Capture;

/// <summary>Draws the animated perimeter of the current native window candidate.</summary>
internal sealed class RainbowOutline : FrameworkElement
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public Rect Bounds { get; set; } = Rect.Empty;
    public double StrokeScale { get; set; } = 1;

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var perimeter = 2 * (Bounds.Width + Bounds.Height);
        var phase = SystemParameters.ClientAreaAnimation ? _clock.Elapsed.TotalSeconds / 2 : 0;
        var corners = new[] { Bounds.TopLeft, Bounds.TopRight, Bounds.BottomRight, Bounds.BottomLeft, Bounds.TopLeft };
        var offset = 0.0;
        for (var edge = 0; edge < 4; edge++)
        {
            var vector = corners[edge + 1] - corners[edge];
            for (var segment = 0; segment < 40; segment++)
            {
                var color = RainbowColor((offset + vector.Length * segment / 40) / perimeter + phase);
                var from = corners[edge] + vector * (segment / 40.0);
                var to = corners[edge] + vector * ((segment + 1) / 40.0);
                drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B)), 9 * StrokeScale), from, to);
                drawingContext.DrawLine(new Pen(new SolidColorBrush(color), 3 * StrokeScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, from, to);
            }
            offset += vector.Length;
        }
    }

    private static Color RainbowColor(double position)
    {
        var hue = (position - Math.Floor(position)) * 6;
        var rising = (byte)(255 * (hue - Math.Floor(hue)));
        var falling = (byte)(255 - rising);
        return (int)hue switch
        {
            0 => Color.FromRgb(255, rising, 0), 1 => Color.FromRgb(falling, 255, 0),
            2 => Color.FromRgb(0, 255, rising), 3 => Color.FromRgb(0, falling, 255),
            4 => Color.FromRgb(rising, 0, 255), _ => Color.FromRgb(255, 0, falling),
        };
    }
}
