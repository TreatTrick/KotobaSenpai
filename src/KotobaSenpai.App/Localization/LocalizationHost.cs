using System.Diagnostics.CodeAnalysis;
using System.Windows;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// Static bridge between the markup extension and the localizer: injects <see cref="IStringLocalizer"/> at startup,
/// tracks all resolved XAML binding targets (weak references), and resets their property values in place on culture switch.
/// XAML markup extensions are instantiated by the parser and cannot go through DI, so injection uses this static bridge.
/// </summary>
internal static class LocalizationHost
{
    private static readonly List<Target> _targets = [];
    private static bool _subscribed;

    public static IStringLocalizer? Localizer { get; set; }

    /// <summary>Resolves localized text by key; returns the key name itself when the localizer is not yet injected or the key is missing.</summary>
    public static string Resolve(string key)
        => Localizer is null ? key : Localizer.Get(key);

    /// <summary>Registers a binding target so it is refreshed in place on culture switch.</summary>
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
