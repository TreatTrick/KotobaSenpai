using System.Windows.Controls;
using System.Windows;
using KotobaSenpai.Core.Overlay;

namespace KotobaSenpai.App.Views;

public partial class RecognitionPage : UserControl
{
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
