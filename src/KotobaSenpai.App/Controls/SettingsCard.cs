using System.Windows;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App.Controls;

/// <summary>A Fluent settings row with a shared title, description, icon, and content layout.</summary>
public sealed class SettingsCard : CardControl
{
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionContentProperty =
        DependencyProperty.Register(nameof(DescriptionContent), typeof(object), typeof(SettingsCard));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? DescriptionContent
    {
        get => GetValue(DescriptionContentProperty);
        set => SetValue(DescriptionContentProperty, value);
    }
}
