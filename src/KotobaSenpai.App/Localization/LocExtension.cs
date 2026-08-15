using System.Windows;
using System.Windows.Markup;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// XAML markup extension: <c>{loc:Loc Key=...}</c> resolves to the current culture's localized text and updates in place
/// after a runtime culture switch, without needing a restart or window rebuild. When the key is missing, the key name is shown so gaps are easy to spot. Markup extensions are instantiated by XAML
/// and cannot go through DI, so the localizer is obtained through the <see cref="LocalizationHost"/> static bridge.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    /// <summary>The resource key to resolve (corresponds to an entry name in Strings.resx).</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var value = LocalizationHost.Resolve(Key);

        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is DependencyObject target
            && pvt.TargetProperty is DependencyProperty property)
        {
            LocalizationHost.Register(target, property, Key);
        }

        return value;
    }
}
