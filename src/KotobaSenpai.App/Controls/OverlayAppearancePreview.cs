using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.App.Controls;

/// <summary>Uses the real annotation primitives for appearance previews and the recognition legend.</summary>
public sealed class OverlayAppearancePreview : Canvas
{
    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(nameof(Appearance),
        typeof(OverlayAppearance), typeof(OverlayAppearancePreview),
        new PropertyMetadata(OverlayAppearanceSettings.Default, (sender, _) => ((OverlayAppearancePreview)sender).Refresh()));
    public OverlayAppearance Appearance
    {
        get => (OverlayAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    public static readonly DependencyProperty ShowPitchNumbersProperty = DependencyProperty.Register(nameof(ShowPitchNumbers),
        typeof(bool), typeof(OverlayAppearancePreview),
        new PropertyMetadata(false, (sender, _) => ((OverlayAppearancePreview)sender).Build()));
    public bool ShowPitchNumbers
    {
        get => (bool)GetValue(ShowPitchNumbersProperty);
        set => SetValue(ShowPitchNumbersProperty, value);
    }

    public OverlayAppearancePreview()
    {
        Width = 360;
        Height = 88;
        IsHitTestVisible = false;
        Build();
    }

    private void Build()
    {
        Children.Clear();
        var style = new OverlayAnnotationStyle(Appearance);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var (sample, left) in new[] { ("日本語", 12d), ("日本語", 140d), ("かな", 268d) })
        {
            var text = new TextBlock { Text = sample, FontSize = 30 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            Add(text, left, 28);
            Add(OverlayAnnotation.CreatePill(sample.Length * 30, 2, OverlayColorRole.Underline, style), left, 68);
        }
        Add(OverlayAnnotation.CreateText("にほんご", 14, OverlayColorRole.Furigana, style, dpi), 24, 9);
        var morae = new[] { "に", "ほ", "ん", "ご" };
        for (var i = 0; i < morae.Length; i++)
            Add(OverlayAnnotation.CreateText(morae[i], 14, i is 0 or 3 ? OverlayColorRole.Low : OverlayColorRole.High, style, dpi),
                154 + i * 14, 9);
        Add(OverlayAnnotation.CreatePill(6, 6, OverlayColorRole.Low, style), 280, 17);
        Add(OverlayAnnotation.CreatePill(6, 6, OverlayColorRole.High, style), 310, 17);
        Add(OverlayAnnotation.CreatePill(5, 2, OverlayColorRole.Heiban, style), 325, 19);
        if (ShowPitchNumbers)
        {
            foreach (var (number, left) in new[] { ("③", 232d), ("⓪", 332d) })
            {
                var text = new TextBlock { Text = number, FontSize = 16 };
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                Add(text, left, 31);
            }
        }
    }

    private void Add(FrameworkElement element, double x, double y)
    {
        Children.Add(element);
        SetLeft(element, x);
        SetTop(element, y);
    }

    private void Refresh()
    {
        var style = new OverlayAnnotationStyle(Appearance);
        foreach (var element in Children.OfType<OverlayAnnotation>())
            element.ApplyStyle(style);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Build();
    }
}
