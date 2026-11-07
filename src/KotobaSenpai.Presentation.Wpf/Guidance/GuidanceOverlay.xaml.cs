using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KotobaSenpai.Presentation.Wpf.Guidance;

/// <summary>Draws one anchored explanation and blocks input to the surface underneath it.</summary>
public sealed partial class GuidanceOverlay : Canvas
{
    private readonly FrameworkElement _bubbleHost;
    private readonly Thickness _regularBubblePadding;
    private readonly Thickness _regularButtonPadding;
    private readonly Thickness _regularButtonMargin;
    private readonly double _regularButtonHeight;

    public GuidanceOverlay() : this(null) { }

    public GuidanceOverlay(Func<Border, Button, FrameworkElement>? decorateBubble)
    {
        InitializeComponent();
        // Keep standalone defaults only for keys the application does not supply.
        var fallbackPalette = Resources.MergedDictionaries[0];
        foreach (var key in fallbackPalette.Keys.Cast<object>().ToArray())
            if (Application.Current?.TryFindResource(key) is Brush) fallbackPalette.Remove(key);
        _regularBubblePadding = _bubble.Padding;
        _regularButtonPadding = AcknowledgeButton.Padding;
        _regularButtonMargin = AcknowledgeButton.Margin;
        _regularButtonHeight = AcknowledgeButton.MinHeight;
        _bubbleHost = _bubble;
        if (decorateBubble is not null)
        {
            Children.Remove(_bubble);
            _bubbleHost = decorateBubble(_bubble, AcknowledgeButton) ?? _bubble;
            Children.Add(_bubbleHost);
        }
    }

    public Button AcknowledgeButton => AcknowledgeAction;
    public event EventHandler? Acknowledged;

    private void OnAcknowledgeClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Acknowledged?.Invoke(this, EventArgs.Empty);
    }

    public void SetText(string text, string acknowledgeText)
    {
        _text.Text = text;
        _acknowledgeText.Text = acknowledgeText;
        AutomationProperties.SetName(this, text);
        AutomationProperties.SetName(AcknowledgeButton, acknowledgeText);
    }

    public void Place(Rect target, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        var bounds = new Rect(area);
        target.Intersect(bounds);
        if (target.IsEmpty) target = new Rect(area.Width / 2, area.Height / 2, 1, 1);
        var hole = target;
        hole.Inflate(5, 5);
        hole.Intersect(bounds);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(bounds));
        mask.Children.Add(new RectangleGeometry(hole, _highlight.CornerRadius.TopLeft, _highlight.CornerRadius.TopLeft));
        _mask.Data = mask;
        SetLeft(_highlight, hole.Left);
        SetTop(_highlight, hole.Top);
        _highlight.Width = hole.Width;
        _highlight.Height = hole.Height;

        var margin = Math.Min(12, Math.Min(area.Width, area.Height) / 8);
        var padding = area.Width < 100 || area.Height < 100 ? new Thickness(2)
            : area.Height < 180 ? new Thickness(8) : _regularBubblePadding;
        var horizontalPadding = padding.Left + padding.Right;
        var verticalPadding = padding.Top + padding.Bottom;
        var maxHeight = Math.Max(1, area.Height - margin * 2);
        var spaceAbove = Math.Max(0, target.Top - margin - 18);
        var spaceBelow = Math.Max(0, area.Height - target.Bottom - margin - 18);
        var placeAbove = spaceAbove >= 140 || spaceAbove >= spaceBelow;
        maxHeight = Math.Min(maxHeight, Math.Max(Math.Min(72, maxHeight), placeAbove ? spaceAbove : spaceBelow));
        var buttonHeight = Math.Min(_regularButtonHeight, Math.Max(1, maxHeight - verticalPadding - 2));
        var gap = Math.Min(_regularButtonMargin.Top,
            Math.Max(0, maxHeight - verticalPadding - buttonHeight - _regularButtonMargin.Bottom - 2));
        _bubble.Padding = padding;
        _bubble.Width = Math.Min(_bubble.MaxWidth, Math.Max(1, area.Width - margin * 2));
        AcknowledgeButton.MinHeight = 0;
        AcknowledgeButton.MaxHeight = buttonHeight;
        AcknowledgeButton.MinHeight = buttonHeight;
        AcknowledgeButton.MaxWidth = Math.Max(1, _bubble.Width - horizontalPadding
            - _regularButtonMargin.Left - _regularButtonMargin.Right - 2);
        AcknowledgeButton.Padding = new Thickness(
            Math.Min(_regularButtonPadding.Left, _bubble.Width / 12), Math.Min(_regularButtonPadding.Top, buttonHeight / 8),
            Math.Min(_regularButtonPadding.Right, _bubble.Width / 12), Math.Min(_regularButtonPadding.Bottom, buttonHeight / 8));
        AcknowledgeButton.Margin = new Thickness(_regularButtonMargin.Left, gap,
            _regularButtonMargin.Right, _regularButtonMargin.Bottom);
        _body.MaxHeight = Math.Max(0, maxHeight - verticalPadding - buttonHeight - gap - _regularButtonMargin.Bottom - 2);
        // ponytail: extremely small targets keep only dismissal visible; a separate host is needed for full text there.
        _body.Visibility = _body.MaxHeight < _text.LineHeight ? Visibility.Collapsed : Visibility.Visible;
        _bubble.Height = double.NaN;
        _bubble.Measure(new Size(_bubble.Width, maxHeight));
        var size = new Size(_bubble.Width, Math.Min(maxHeight, _bubble.DesiredSize.Height));
        _bubble.Height = size.Height;
        var center = target.Left + target.Width / 2;
        var left = Math.Clamp(center - size.Width / 2, margin, Math.Max(margin, area.Width - size.Width - margin));
        var above = target.Top - size.Height - 18;
        var top = placeAbove ? above : target.Bottom + 18;
        top = Math.Clamp(top, margin, Math.Max(margin, area.Height - size.Height - margin));
        SetLeft(_bubbleHost, left);
        SetTop(_bubbleHost, top);
        var tip = new Point(center, placeAbove ? target.Top - 5 : target.Bottom + 5);
        var inset = Math.Min(18, size.Width / 2);
        var attachX = Math.Clamp(center, left + inset, left + size.Width - inset);
        var attachY = placeAbove ? top + size.Height : top;
        _arrow.Points = new PointCollection { new(attachX - 8, attachY), tip, new(attachX + 8, attachY) };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            if (!e.IsRepeat) Acknowledged?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
