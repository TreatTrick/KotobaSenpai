using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Views;

public partial class OnboardingPage : UserControl
{
    private OnboardingViewModel? _observed;

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Observe();
        DataContextChanged += (_, _) => { if (IsLoaded) Observe(); };
        Unloaded += (_, _) => { Detach(); StopTransition(); };
    }

    private void Observe()
    {
        Detach();
        _observed = (DataContext as MainWindowViewModel)?.Onboarding;
        if (_observed is not null) _observed.PropertyChanged += OnStepChanged;
        ResetScroll();
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OnboardingViewModel.Step)) return;
        ResetScroll();
        StopTransition();
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        StepBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        ((TranslateTransform)StepBody.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    private void StopTransition()
    {
        StepBody.BeginAnimation(OpacityProperty, null);
        ((TranslateTransform)StepBody.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void ResetScroll() => StepScroll.ScrollToTop();

    private void Detach()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnStepChanged;
        _observed = null;
    }
}
