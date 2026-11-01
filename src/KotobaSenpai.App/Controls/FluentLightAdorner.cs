using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace KotobaSenpai.App.Controls;

/// <summary>Draws light over existing surfaces without changing layout, templates, or hit testing.</summary>
internal sealed class FluentLightAdorner : Adorner
{
    private static readonly LinearGradientBrush HaloBrush = CreateHaloBrush();
    private static readonly (Pen Pen, double Opacity)[] HaloStrokes =
    [
        (HaloPen(11), 0.018), (HaloPen(9), 0.025),
        (HaloPen(7), 0.04), (HaloPen(5), 0.065),
        (HaloPen(3), 0.11), (HaloPen(1), 0.5),
    ];

    private readonly IEnumerable<Surface> _surfaces;
    private Point _pointer;
    private double _presence;
    private bool _dark;

    internal FluentLightAdorner(UIElement adornedElement, IEnumerable<Surface> surfaces) : base(adornedElement)
    {
        _surfaces = surfaces;
        IsHitTestVisible = false;
        Focusable = false;
    }

    internal void Update(Point pointer, double presence, bool dark)
    {
        _pointer = pointer;
        _presence = presence;
        _dark = dark;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        foreach (var surface in _surfaces)
        {
            if (!surface.Element.IsEnabled || !surface.Element.IsVisible || surface.Clip.Rect.IsEmpty) continue;
            var proximity = _presence * FluentLightMotion.Proximity(surface.Bounds, _pointer);
            if (proximity == 0 && surface.Hover == 0) continue;
            var local = _pointer - (Vector)surface.Bounds.TopLeft;
            surface.Edge.Center = surface.Edge.GradientOrigin = local;
            surface.Wash.ImageSource = FluentLightWash.ForTheme(_dark);
            surface.Wash.Viewport = new Rect(local.X - FluentLightWash.Radius, local.Y - FluentLightWash.Radius,
                FluentLightWash.Radius * 2, FluentLightWash.Radius * 2);
            surface.Edge.GradientStops[0].Color = _dark ? Color.FromRgb(179, 200, 255) : Color.FromRgb(76, 112, 235);
            context.PushClip(surface.Clip);
            context.PushTransform(surface.Transform);
            context.PushOpacity(proximity * 0.85);
            context.DrawRoundedRectangle(surface.Wash, null, surface.LocalBounds, surface.Radius, surface.Radius);
            context.DrawRoundedRectangle(null, surface.EdgePen, surface.BorderBounds, surface.Radius, surface.Radius);
            context.Pop();
            if (surface.Hover > 0)
            {
                // Nested translucent strokes approximate a soft halo without blurring text or the full page.
                foreach (var (pen, opacity) in HaloStrokes)
                {
                    context.PushOpacity(surface.Hover * opacity * 0.85);
                    context.DrawRoundedRectangle(null, pen, surface.BorderBounds, surface.Radius, surface.Radius);
                    context.Pop();
                }
            }
            context.Pop();
            context.Pop();
        }
    }

    private static LinearGradientBrush CreateHaloBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1), EndPoint = new Point(1, 0),
            GradientStops =
            [
                new GradientStop(Color.FromRgb(97, 216, 255), 0),
                new GradientStop(Color.FromRgb(124, 157, 255), 0.35),
                new GradientStop(Color.FromRgb(182, 155, 255), 0.7),
                new GradientStop(Color.FromRgb(234, 156, 206), 1),
            ],
        };
        brush.Freeze();
        return brush;
    }

    private static Pen HaloPen(double thickness)
    {
        var pen = new Pen(HaloBrush, thickness);
        pen.Freeze();
        return pen;
    }

    internal sealed class Surface(FrameworkElement element)
    {
        internal FrameworkElement Element { get; } = element;
        internal FluentLightKind Kind => FluentLight.GetKind(Element);
        internal double Radius => FluentLight.GetCornerRadius(Element);
        internal Rect Bounds { get; private set; } = Rect.Empty;
        internal Rect LocalBounds { get; private set; }
        internal Rect BorderBounds { get; private set; }
        internal RectangleGeometry Clip { get; } = new(Rect.Empty);
        internal TranslateTransform Transform { get; } = new();
        internal RadialGradientBrush Edge { get; } = CreateRadial(135, 0.95);
        internal ImageBrush Wash { get; } = new()
        {
            ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, TileMode = TileMode.None,
        };
        internal Pen EdgePen { get; } = new();
        internal double Hover { get; set; }

        internal bool Measure(UIElement root)
        {
            if (!Element.IsVisible || Element.ActualWidth <= 1 || Element.ActualHeight <= 1 || !root.IsAncestorOf(Element))
            {
                var wasVisible = !Bounds.IsEmpty;
                Bounds = Rect.Empty;
                Clip.Rect = Rect.Empty;
                return wasVisible;
            }
            var bounds = Element.TransformToAncestor(root).TransformBounds(new Rect(Element.RenderSize));
            var clip = new Rect(root.RenderSize);
            // Intersect rectangular viewport clips so scrolled-off cards cannot illuminate other UI.
            for (DependencyObject? ancestor = Element; ancestor is not null && !ReferenceEquals(ancestor, root);
                 ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not UIElement visual) continue;
                var transform = visual.TransformToAncestor(root);
                if (visual.ClipToBounds) clip.Intersect(transform.TransformBounds(new Rect(visual.RenderSize)));
                if (VisualTreeHelper.GetClip(visual) is { } geometry) clip.Intersect(transform.TransformBounds(geometry.Bounds));
            }
            if (Bounds == bounds && Clip.Rect == clip) return false;
            Bounds = bounds;
            Clip.Rect = clip;
            Transform.X = Bounds.X;
            Transform.Y = Bounds.Y;
            LocalBounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            BorderBounds = new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
            EdgePen.Brush = Edge;
            EdgePen.Thickness = 1;
            return true;
        }

        private static RadialGradientBrush CreateRadial(double radius, double opacity) => new()
        {
            MappingMode = BrushMappingMode.Absolute, RadiusX = radius, RadiusY = radius, Opacity = opacity,
            GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(Colors.Transparent, 1)],
        };
    }
}
