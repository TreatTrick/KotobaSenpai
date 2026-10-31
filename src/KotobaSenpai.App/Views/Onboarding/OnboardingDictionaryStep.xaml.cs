using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KotobaSenpai.App.Views.Onboarding;

public partial class OnboardingDictionaryStep : UserControl
{
    private bool _isRainbowBorderLoaded;
    private RotateTransform? _borderRotation;

    public OnboardingDictionaryStep()
    {
        InitializeComponent();
    }

    private void OnRainbowProgressBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border) return;
        _isRainbowBorderLoaded = true;
        if (_borderRotation is null && border.BorderBrush is LinearGradientBrush brush)
        {
            _borderRotation = new RotateTransform { CenterX = 0.5, CenterY = 0.5 };
            var mutableBrush = brush.CloneCurrentValue();
            mutableBrush.RelativeTransform = _borderRotation;
            border.BorderBrush = mutableBrush;
        }

        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateBorderAnimation();
    }

    private void OnRainbowProgressBorderUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _isRainbowBorderLoaded = false;
        if (_borderRotation is null) return;
        _borderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _borderRotation.Angle = 0;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))) return;
        _ = Dispatcher.InvokeAsync(UpdateBorderAnimation);
    }

    private void UpdateBorderAnimation()
    {
        if (_borderRotation is null) return;
        _borderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _borderRotation.Angle = 0;
        if (!_isRainbowBorderLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;

        _borderRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });
    }
}
