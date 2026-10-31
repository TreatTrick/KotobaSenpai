using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.Onboarding;

namespace KotobaSenpai.App.Views.Onboarding;

/// <summary>Maps a flow step to its complete view; all navigation remains in the state machine.</summary>
public sealed class OnboardingStepTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Dictionary { get; set; }
    public DataTemplate? Model { get; set; }
    public DataTemplate? Window { get; set; }
    public DataTemplate? Ready { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        OnboardingStep.Dictionary => Dictionary,
        OnboardingStep.Model => Model,
        OnboardingStep.Window => Window,
        OnboardingStep.Ready => Ready,
        _ => null,
    };
}
