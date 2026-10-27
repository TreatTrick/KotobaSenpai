using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KotobaSenpai.App.Controls;

/// <summary>A saturation/brightness field with a separate hue, mouse dragging and arrow-key selection.</summary>
public sealed class ColorSpectrum : FrameworkElement
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color),
        typeof(ColorSpectrum), new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) => ((ColorSpectrum)sender).ReadColor()));
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double),
        typeof(ColorSpectrum), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender,
            (sender, _) => ((ColorSpectrum)sender).WriteColor(),
            (_, value) => double.IsFinite((double)value) ? Math.Clamp((double)value, 0, 360) : 0d));

    private static readonly LinearGradientBrush SaturationGradient = Gradient(Colors.White, Color.FromArgb(0, 255, 255, 255), false);
    private static readonly LinearGradientBrush BrightnessGradient = Gradient(Color.FromArgb(0, 0, 0, 0), Colors.Black, true);
    private double _saturation = 1;
    private double _brightness = 1;
    private bool _updating;

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
    public double Hue
    {
        get => (double)GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }
    public event EventHandler? ColorChanged;

    public ColorSpectrum()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        Cursor = Cursors.Cross;
        MinWidth = 120;
        MinHeight = 100;
    }

    private Rect Field => new(6, 6, Math.Max(0, ActualWidth - 12), Math.Max(0, ActualHeight - 12));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var field = Field;
        drawingContext.DrawRectangle(new SolidColorBrush(FromHsv(Hue, 1, 1)), null, field);
        drawingContext.DrawRectangle(SaturationGradient, null, field);
        drawingContext.DrawRectangle(BrightnessGradient, new Pen(Brushes.Gray, 1), field);
        if (IsKeyboardFocused)
            drawingContext.DrawRectangle(null, new Pen(SystemColors.HighlightBrush, 2),
                new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4)));
        var marker = new Point(field.X + _saturation * field.Width, field.Y + (1 - _brightness) * field.Height);
        drawingContext.DrawEllipse(null, new Pen(Brushes.Black, 2), marker, 5, 5);
        drawingContext.DrawEllipse(null, new Pen(Brushes.White, 1), marker, 4, 4);
    }

    internal void SelectAt(Point position)
    {
        var field = Field;
        if (field.Width == 0 || field.Height == 0)
            return;
        _saturation = Math.Clamp((position.X - field.X) / field.Width, 0, 1);
        _brightness = 1 - Math.Clamp((position.Y - field.Y) / field.Height, 0, 1);
        WriteColor();
    }

    private void ReadColor()
    {
        if (_updating)
            return;
        var hsv = ToHsv(Color);
        _brightness = hsv.Brightness;
        // Black and gray do not define a hue; preserve the user's hue and black's saturation.
        if (hsv.Brightness > 0)
            _saturation = hsv.Saturation;
        if (hsv.Saturation > 0)
        {
            _updating = true;
            try { SetCurrentValue(HueProperty, hsv.Hue); }
            finally { _updating = false; }
        }
        InvalidateVisual();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WriteColor()
    {
        if (_updating)
            return;
        _updating = true;
        try { SetCurrentValue(ColorProperty, FromHsv(Hue, _saturation, _brightness)); }
        finally { _updating = false; }
        InvalidateVisual();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        SelectAt(e.GetPosition(this));
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            SelectAt(e.GetPosition(this));
            e.Handled = true;
        }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            SelectAt(e.GetPosition(this));
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: _saturation = Math.Max(0, _saturation - step); break;
            case Key.Right: _saturation = Math.Min(1, _saturation + step); break;
            case Key.Up: _brightness = Math.Min(1, _brightness + step); break;
            case Key.Down: _brightness = Math.Max(0, _brightness - step); break;
            default: base.OnKeyDown(e); return;
        }
        WriteColor();
        e.Handled = true;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private static LinearGradientBrush Gradient(Color start, Color end, bool vertical)
    {
        var brush = new LinearGradientBrush(start, end, new Point(), new Point(vertical ? 0 : 1, vertical ? 1 : 0));
        brush.Freeze();
        return brush;
    }

    internal static Color FromHsv(double hue, double saturation, double brightness)
    {
        var chroma = brightness * saturation;
        var sector = hue % 360 / 60;
        var secondary = chroma * (1 - Math.Abs(sector % 2 - 1));
        var minimum = brightness - chroma;
        var (red, green, blue) = sector switch
        {
            < 1 => (chroma, secondary, 0d), < 2 => (secondary, chroma, 0d),
            < 3 => (0d, chroma, secondary), < 4 => (0d, secondary, chroma),
            < 5 => (secondary, 0d, chroma), _ => (chroma, 0d, secondary),
        };
        static byte Channel(double value) => (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        return Color.FromRgb(Channel(red + minimum), Channel(green + minimum), Channel(blue + minimum));
    }

    internal static (double Hue, double Saturation, double Brightness) ToHsv(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var hue = delta == 0 ? 0 : maximum == red ? 60 * ((green - blue) / delta % 6)
            : maximum == green ? 60 * ((blue - red) / delta + 2) : 60 * ((red - green) / delta + 4);
        return ((hue + 360) % 360, maximum == 0 ? 0 : delta / maximum, maximum);
    }
}
