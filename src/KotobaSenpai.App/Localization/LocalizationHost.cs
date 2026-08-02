using System.Diagnostics.CodeAnalysis;
using System.Windows;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// 标记扩展与本地化器之间的静态桥：在启动时注入 <see cref="IStringLocalizer"/>，
/// 跟踪所有已解析的 XAML 绑定目标（弱引用），文化切换时就地重设属性值。
/// XAML 标记扩展由解析器实例化、无法走 DI，故用静态桥注入。
/// </summary>
internal static class LocalizationHost
{
    private static readonly List<Target> _targets = [];
    private static bool _subscribed;

    public static IStringLocalizer? Localizer { get; set; }

    /// <summary>按键解析本地化文本；本地化器尚未注入或键缺失时返回键名本身。</summary>
    public static string Resolve(string key)
        => Localizer is null ? key : Localizer.Get(key);

    /// <summary>登记一个绑定目标，使其在文化切换时被就地刷新。</summary>
    public static void Register(DependencyObject target, DependencyProperty property, string key)
    {
        EnsureSubscribed();
        _targets.Add(new Target(target, property, key));
    }

    private static void EnsureSubscribed()
    {
        if (_subscribed || Localizer is null)
            return;

        Localizer.CultureChanged += OnCultureChanged;
        _subscribed = true;
    }

    private static void OnCultureChanged(object? sender, EventArgs e)
    {
        if (Localizer is null)
            return;

        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            var target = _targets[i];
            if (!target.TryGetTarget(out var dependencyObject))
            {
                _targets.RemoveAt(i);
                continue;
            }

            dependencyObject.SetValue(target.Property, Localizer.Get(target.Key));
        }
    }

    private readonly struct Target
    {
        private readonly WeakReference<DependencyObject> _reference;

        public Target(DependencyObject target, DependencyProperty property, string key)
        {
            _reference = new WeakReference<DependencyObject>(target);
            Property = property;
            Key = key;
        }

        public DependencyProperty Property { get; }

        public string Key { get; }

        public bool TryGetTarget([MaybeNullWhen(false)] out DependencyObject target)
            => _reference.TryGetTarget(out target);
    }
}
