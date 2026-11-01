using System.Windows;
using System.Windows.Media;

namespace KotobaSenpai.App.Controls;

public enum FluentLightKind { None, Surface, Button }

/// <summary>Marks existing controls for decoration by their nearest light host.</summary>
public static class FluentLight
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(FluentLightKind), typeof(FluentLight), new PropertyMetadata(FluentLightKind.None, OnKindChanged));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(double), typeof(FluentLight), new PropertyMetadata(4d),
        value => value is double radius && double.IsFinite(radius) && radius >= 0);

    private static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached(
        "Host", typeof(FluentLightHost), typeof(FluentLight));

    public static FluentLightKind GetKind(DependencyObject element) => (FluentLightKind)element.GetValue(KindProperty);
    public static void SetKind(DependencyObject element, FluentLightKind value) => element.SetValue(KindProperty, value);
    public static double GetCornerRadius(DependencyObject element) => (double)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, double value) => element.SetValue(CornerRadiusProperty, value);

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        Detach(element);
        if ((FluentLightKind)e.NewValue == FluentLightKind.None) return;
        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        if (element.IsLoaded) Attach(element);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((FrameworkElement)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((FrameworkElement)sender);

    private static void Attach(FrameworkElement element)
    {
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(element); ancestor is not null;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FluentLightHost host) continue;
            if (ReferenceEquals(element.GetValue(HostProperty), host)) return;
            Detach(element);
            element.SetValue(HostProperty, host);
            host.Register(element);
            return;
        }
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.GetValue(HostProperty) is not FluentLightHost host) return;
        host.Unregister(element);
        element.ClearValue(HostProperty);
    }
}
