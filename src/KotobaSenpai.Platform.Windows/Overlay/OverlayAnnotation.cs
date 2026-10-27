using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Overlay;

public enum OverlayColorRole { Furigana, High, Low, Underline, Hover, Heiban }

/// <summary>Shares frozen brushes and pens across all annotations in one appearance snapshot.</summary>
public sealed class OverlayAnnotationStyle
{
    private readonly Brush[] _fills;
    private readonly Pen?[] _outlines;
    public OverlayAppearance Appearance { get; }

    public OverlayAnnotationStyle(OverlayAppearance appearance)
    {
        Appearance = appearance;
        string[] colors = [appearance.FuriganaColor, appearance.PitchHighColor, appearance.PitchLowColor,
            appearance.UnderlineColor, "#FF4500", "#88FF88"];
        _fills = colors.Select(CreateBrush).ToArray();
        _outlines = colors.Select(color =>
        {
            if (!appearance.OutlineEnabled)
                return null;
            // Drawing twice the requested width behind the fill leaves that width visible outside the geometry.
            var pen = new Pen(CreateBrush(OverlayAppearanceSettings.GetOutlineColor(color, appearance.OutlineColorMode)),
                appearance.OutlineThickness * 2)
                { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }).ToArray();
    }

    internal Brush Fill(OverlayColorRole role) => _fills[(int)role];
    internal Pen? Outline(OverlayColorRole role) => _outlines[(int)role];

    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Draws glyphs or rounded markers without changing their layout or mouse hit regions.</summary>
public sealed class OverlayAnnotation : FrameworkElement
{
    private OverlayAnnotationStyle _style;
    private OverlayColorRole _role;
    internal Geometry? Glyph { get; private init; }

    private OverlayAnnotation(double width, double height, OverlayColorRole role, OverlayAnnotationStyle style)
    {
        Width = width;
        Height = height;
        _role = role;
        _style = style;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public OverlayColorRole Role
    {
        get => _role;
        set { if (_role != value) { _role = value; InvalidateVisual(); } }
    }

    public static OverlayAnnotation CreateText(string text, double fontSize, OverlayColorRole role,
        OverlayAnnotationStyle style, double pixelsPerDip = 1)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, pixelsPerDip);
        var glyph = formatted.BuildGeometry(new Point());
        glyph.Freeze();
        return new OverlayAnnotation(formatted.WidthIncludingTrailingWhitespace, formatted.Height, role, style) { Glyph = glyph };
    }

    public static OverlayAnnotation CreatePill(double width, double height, OverlayColorRole role, OverlayAnnotationStyle style)
        => new(width, height, role, style);

    public void ApplyStyle(OverlayAnnotationStyle style)
    {
        _style = style;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var outline = _style.Outline(Role);
        var fill = _style.Fill(Role);
        if (Glyph is { } glyph)
        {
            if (outline is not null)
                drawingContext.DrawGeometry(null, outline, glyph);
            drawingContext.DrawGeometry(fill, null, glyph);
        }
        else
        {
            var rect = new Rect(0, 0, Width, Height);
            if (outline is not null)
                drawingContext.DrawRoundedRectangle(null, outline, rect, Height / 2, Height / 2);
            drawingContext.DrawRoundedRectangle(fill, null, rect, Height / 2, Height / 2);
        }
    }
}
