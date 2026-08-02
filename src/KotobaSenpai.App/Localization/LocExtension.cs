using System.Windows;
using System.Windows.Markup;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// XAML 标记扩展：<c>{loc:Loc Key=...}</c> 解析为当前文化的本地化文本，并在运行时文化切换后
/// 就地更新，无需重启或重建窗口。键缺失时显示键名以便发现缺口。标记扩展由 XAML 实例化、
/// 无法走 DI，故通过 <see cref="LocalizationHost"/> 静态桥获取本地化器。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    /// <summary>要解析的资源键（对应 Strings.resx 中的条目名）。</summary>
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
