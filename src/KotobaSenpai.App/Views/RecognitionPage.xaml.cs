using System.Windows.Controls;
using System.Windows;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Views;

public partial class RecognitionPage : UserControl
{
    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(nameof(Appearance),
        typeof(OverlayAppearance), typeof(RecognitionPage), new PropertyMetadata(OverlayAppearanceSettings.Default));

    public OverlayAppearance Appearance
    {
        get => (OverlayAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    public RecognitionPage() => InitializeComponent();

    internal FrameworkElement? FindGuideTarget(GuidanceTarget target) => target switch
    {
        GuidanceTarget.SelectWindow => WindowSelection.SelectWindowButton,
        GuidanceTarget.Recognize => RecognizeButton,
        GuidanceTarget.RecognitionRegion => RegionButton,
        GuidanceTarget.AutoRecognition => AutoRecognitionToggle,
        _ => null,
    };
}
