using System.Windows;
using System.Windows.Markup;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// XAML markup extension: <c>{loc:Loc Key=...}</c> resolves to the current culture's localized text and updates in place
/// after a runtime culture switch, without needing a restart or window rebuild. When the key is missing, the key name is shown so gaps are easy to spot. Markup extensions are instantiated by XAML
/// and locate their scoped <see cref="LocalizationHost"/> through WPF resources.
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
        LocalizationHost? host = null;

        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt)
        {
            // Defer template placeholders until WPF creates the actual target instance.
            if (pvt.TargetObject is not DependencyObject)
                return this;
            if (pvt.TargetObject is DependencyObject target && pvt.TargetProperty is DependencyProperty property)
            {
                host = LocalizationHost.Find(target);
                host?.Register(target, property, Key);
            }
        }

        return (host ?? LocalizationHost.Find(null))?.Resolve(Key) ?? Key;
    }
}
