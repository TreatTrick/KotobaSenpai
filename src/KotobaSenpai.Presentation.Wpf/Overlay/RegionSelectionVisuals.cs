using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Presentation.Wpf.Guidance;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Draws region editing and passive feedback using the same retained visual elements.</summary>
internal sealed class RegionSelectionVisuals
{
    private static readonly Brush DimBrush = (Brush)new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)).GetAsFrozen();
    private static readonly Brush RegionInputBrush = (Brush)new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)).GetAsFrozen();
    private static readonly Brush HandleBrush = (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x00)).GetAsFrozen();

    private readonly GuidanceOverlay _guideOverlay;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Button _confirmButton;
    private readonly RotateTransform _confirmBorderRotation = new(0, 0.5, 0.5);
    private readonly LinearGradientBrush _confirmBorderBrush;
    private readonly Grid _flashOutline = CreateWarningOutline();
    private readonly TextBlock _feedbackText = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 24,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x79, 0x89)),
    };
    private readonly Border _feedbackMessage;

    private readonly IStringLocalizer? _localizer;
    private readonly ITourPresentation? _guidance;
    private WindowTarget? _target;
    private ScreenRect _region;
    private double _scale = 1;
    private bool _isFlashing;

    public Canvas Canvas => _canvas;
    public Button ConfirmButton => _confirmButton;
    public Grid FlashOutline => _flashOutline;

    public RegionSelectionVisuals(IStringLocalizer? localizer, ITourPresentation? guidance,
        Func<Border, Button, FrameworkElement>? decorateBubble)
    {
        _localizer = localizer;
        _guidance = guidance;
        _guideOverlay = new GuidanceOverlay(decorateBubble);
        _guideOverlay.Acknowledged += (_, _) => _guidance?.Advance();
        _confirmBorderBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0xEF, 0x47, 0x6F), 0),
            new(Color.FromRgb(0xFF, 0x9F, 0x1C), 0.17),
            new(Color.FromRgb(0xFF, 0xD1, 0x66), 0.33),
            new(Color.FromRgb(0x06, 0xD6, 0xA0), 0.5),
            new(Color.FromRgb(0x11, 0x8A, 0xB2), 0.67),
            new(Color.FromRgb(0xA8, 0x55, 0xF7), 0.83),
            new(Color.FromRgb(0xEF, 0x47, 0x6F), 1),
        }, new Point(0, 0), new Point(1, 1)) { RelativeTransform = _confirmBorderRotation };
        _confirmButton = CreateConfirmButton();
        _feedbackMessage = CreateWarningMessage(_feedbackText);
    }

    private static Button CreateConfirmButton()
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8));
        template.Triggers.Add(pressed);
        return new Button
        {
            Padding = new Thickness(14, 6, 14, 6),
            BorderThickness = new Thickness(2.5),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = template,
        };
    }

    public void UpdateConfirmAnimation(bool animate)
    {
        _confirmBorderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _confirmBorderRotation.Angle = 0;
        _confirmButton.Background = SystemParameters.HighContrast
            ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
        _confirmButton.Foreground = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brushes.White;
        _confirmButton.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : _confirmBorderBrush;
        if (!animate || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;
        _confirmBorderRotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static Grid CreateWarningOutline()
    {
        var outline = new Grid { IsHitTestVisible = false };
        outline.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x50)),
            StrokeThickness = 3,
            RadiusX = 6,
            RadiusY = 6,
            Margin = new Thickness(2),
            Effect = CreateWarningGlow(16, 0.95),
        });
        outline.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0xDC)),
            StrokeThickness = 1,
            RadiusX = 5,
            RadiusY = 5,
            Margin = new Thickness(3),
        });
        return outline;
    }

    private static DropShadowEffect CreateWarningGlow(double blurRadius, double opacity)
    {
        var glow = new DropShadowEffect
        {
            Color = Color.FromRgb(0xFF, 0x2A, 0x40),
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Opacity = opacity,
            RenderingBias = RenderingBias.Quality,
        };
        glow.Freeze();
        return glow;
    }

    private static Border CreateWarningMessage(TextBlock text)
    {
        var pen = new Pen(text.Foreground, 1.75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(12, 12), 10, 10)));
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M8,8 L16,16 M16,8 L8,16")));
        drawing.Freeze();
        var content = new Grid { Effect = CreateWarningGlow(8, 0.8) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new Image
        {
            Source = new DrawingImage(drawing), Width = 24, Height = 24,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return new Border
        {
            Child = content, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x19, 0x1C, 0x24)), IsHitTestVisible = false,
        };
    }

    public void Render(WindowTarget target, ScreenRect region, double scale, bool flashing)
    {
        _target = target;
        _region = region;
        _scale = scale;
        _isFlashing = flashing;
        _canvas.Children.Clear();
        double winW = target.Bounds.Width / _scale;
        double winH = target.Bounds.Height / _scale;
        double sx = _region.X / _scale, sy = _region.Y / _scale;
        double sw = _region.Width / _scale, sh = _region.Height / _scale;

        if (_isFlashing)
        {
            _flashOutline.Width = sw;
            _flashOutline.Height = sh;
            Canvas.SetLeft(_flashOutline, sx);
            Canvas.SetTop(_flashOutline, sy);
            _canvas.Children.Add(_flashOutline);
            _feedbackText.Text = _localizer?.Get("Region_NoJapanese")
                ?? "No recognizable Japanese was found in this area. Please select a different area.";
            _feedbackMessage.MaxWidth = Math.Min(560, Math.Min(Math.Max(0, winW - 24), Math.Max(280, sw - 32)));
            _feedbackMessage.MaxHeight = Math.Max(0, winH - 24);
            _feedbackMessage.Measure(new Size(_feedbackMessage.MaxWidth, _feedbackMessage.MaxHeight));
            var messageSize = _feedbackMessage.DesiredSize;
            Canvas.SetLeft(_feedbackMessage, Math.Clamp(sx + (sw - messageSize.Width) / 2,
                12, Math.Max(12, winW - messageSize.Width - 12)));
            Canvas.SetTop(_feedbackMessage, Math.Clamp(sy + (sh - messageSize.Height) / 2,
                12, Math.Max(12, winH - messageSize.Height - 12)));
            _canvas.Children.Add(_feedbackMessage);
            return;
        }

        // Layered windows pass mouse input through pixels with zero alpha.
        AddRect(sx, sy, sw, sh, RegionInputBrush);

        // Dim the four areas outside the region so the capture range stands out.
        AddRect(0, 0, winW, sy);
        AddRect(0, sy + sh, winW, winH - sy - sh);
        AddRect(0, sy, sx, sh);
        AddRect(sx + sw, sy, winW - sx - sw, sh);

        // Four L-shaped corner brackets.
        DrawHandle(sx, sy, +1, +1);
        DrawHandle(sx + sw, sy, -1, +1);
        DrawHandle(sx, sy + sh, +1, -1);
        DrawHandle(sx + sw, sy + sh, -1, -1);

        // Reuse the button so geometry updates do not restart its rainbow animation.
        _confirmButton.Content = _localizer?.Get("Region_Confirm") ?? "OK";
        AutomationProperties.SetName(_confirmButton, (string)_confirmButton.Content);
        _confirmButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_confirmButton, sx + sw / 2 - _confirmButton.DesiredSize.Width / 2);
        Canvas.SetTop(_confirmButton, sy + sh / 2 - _confirmButton.DesiredSize.Height / 2);
        _canvas.Children.Add(_confirmButton);
        DrawGuidance();
    }

    public void DrawGuidance()
    {
        _canvas.Children.Remove(_guideOverlay);
        if (_guidance?.Target is not { } anchor || _target is null || _isFlashing) return;
        var area = new Size(_target.Bounds.Width / _scale, _target.Bounds.Height / _scale);
        _guideOverlay.Width = area.Width;
        _guideOverlay.Height = area.Height;
        var target = anchor == GuidanceTarget.RegionTopLeft
            ? new Rect(_region.X / _scale, _region.Y / _scale, 20, 20)
            : new Rect(Canvas.GetLeft(_confirmButton), Canvas.GetTop(_confirmButton),
                _confirmButton.DesiredSize.Width, _confirmButton.DesiredSize.Height);
        _guideOverlay.SetText(_guidance.Text, _guidance.AcknowledgeText);
        _guideOverlay.Place(target, area);
        _canvas.Children.Add(_guideOverlay);
    }

    private void AddRect(double x, double y, double w, double h, Brush? fill = null)
    {
        if (w <= 0 || h <= 0)
            return;
        var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = fill ?? DimBrush };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _canvas.Children.Add(rect);
    }

    private void DrawHandle(double cx, double cy, int dirX, int dirY)
    {
        var horizontalLength = Math.Min(RegionSelectionGeometry.ArmLength, _region.Width / _scale / 2);
        var verticalLength = Math.Min(RegionSelectionGeometry.ArmLength, _region.Height / _scale / 2);
        var thickness = RegionSelectionGeometry.ArmThickness;
        AddArm(cx, dirY < 0 ? cy - thickness : cy, horizontalLength * dirX, thickness);
        AddArm(dirX < 0 ? cx - thickness : cx, cy, thickness, verticalLength * dirY);
    }

    private void AddArm(double x, double y, double w, double h)
    {
        var arm = new System.Windows.Shapes.Rectangle
        {
            Width = Math.Abs(w),
            Height = Math.Abs(h),
            Fill = HandleBrush,
            RadiusX = 1,
            RadiusY = 1,
        };
        Canvas.SetLeft(arm, w < 0 ? x + w : x);
        Canvas.SetTop(arm, h < 0 ? y + h : y);
        _canvas.Children.Add(arm);
    }
}
