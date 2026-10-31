using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KotobaSenpai.App.Views.Onboarding;

/// <summary>Shares the approved two-column composition without deciding which step is active.</summary>
public partial class OnboardingStepLayout : UserControl
{
    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(object), typeof(OnboardingStepLayout));
    public static readonly DependencyProperty VisualProperty = DependencyProperty.Register(
        nameof(Visual), typeof(object), typeof(OnboardingStepLayout));

    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public object? Visual
    {
        get => GetValue(VisualProperty);
        set => SetValue(VisualProperty, value);
    }

    public OnboardingStepLayout()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) StepHeading.Focus();
        }, DispatcherPriority.Loaded);
    }
}
