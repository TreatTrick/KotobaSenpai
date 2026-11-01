using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.Controls;

namespace KotobaSenpai.App.Guidance.Presentation;

internal static class GuidanceLight
{
    internal static FrameworkElement DecorateBubble(Border bubble, Button button, bool reactToPointerWhenInactive = false)
    {
        FluentLight.SetKind(bubble, FluentLightKind.Surface);
        FluentLight.SetCornerRadius(bubble, bubble.CornerRadius.TopLeft);
        FluentLight.SetKind(button, FluentLightKind.Button);
        return new FluentLightHost { Child = bubble, ReactToPointerWhenInactive = reactToPointerWhenInactive };
    }
}
