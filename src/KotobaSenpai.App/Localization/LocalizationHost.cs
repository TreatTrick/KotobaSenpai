using System.Diagnostics.CodeAnalysis;
using System.Windows;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>Owns XAML localization targets for one resource scope and releases its culture subscription.</summary>
internal sealed class LocalizationHost : IDisposable
{
    internal const string ResourceKey = "KotobaSenpai.Localization";
    private readonly List<Target> _targets = [];
    private bool _disposed;

    public LocalizationHost(IStringLocalizer localizer)
    {
        Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Localizer.CultureChanged += OnCultureChanged;
    }

    public IStringLocalizer Localizer { get; }

    public static LocalizationHost? Find(DependencyObject? target)
        => (target as FrameworkElement)?.TryFindResource(ResourceKey) as LocalizationHost
            ?? (target as FrameworkContentElement)?.TryFindResource(ResourceKey) as LocalizationHost
            ?? Application.Current?.TryFindResource(ResourceKey) as LocalizationHost;

    /// <summary>Resolves localized text using the localizer owned by this resource scope.</summary>
    public string Resolve(string key) => Localizer.Get(key);

    /// <summary>Registers a binding target so it is refreshed in place on culture switch.</summary>
    public void Register(DependencyObject target, DependencyProperty property, string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _targets.Add(new Target(target, property, key));
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Localizer.CultureChanged -= OnCultureChanged;
        _targets.Clear();
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
