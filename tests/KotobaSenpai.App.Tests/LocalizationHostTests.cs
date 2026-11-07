using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using KotobaSenpai.App.Localization;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Tests;

public sealed class LocalizationHostTests
{
    [Fact]
    public Task Resource_scopes_resolve_and_refresh_independently_and_release_culture_subscriptions() => RunSta(() =>
    {
        var first = new TestLocalizer("First");
        var second = new TestLocalizer("Second");
        using var firstHost = new LocalizationHost(first);
        using var secondHost = new LocalizationHost(second);
        var firstText = LocalizedText(firstHost);
        var secondText = LocalizedText(secondHost);

        Assert.Equal("First:Title", firstText.Text);
        Assert.Equal("Second:Title", secondText.Text);
        first.Change("Updated");
        Assert.Equal("Updated:Title", firstText.Text);
        Assert.Equal("Second:Title", secondText.Text);

        firstHost.Dispose();
        firstHost.Dispose();
        Assert.Equal(0, first.Subscribers);
        Assert.Equal(1, second.Subscribers);
        first.Change("Disposed");
        second.Change("Active");
        Assert.Equal("Updated:Title", firstText.Text);
        Assert.Equal("Active:Title", secondText.Text);
    });

    [Fact]
    public Task Markup_extension_defers_shared_template_placeholders() => RunSta(() =>
    {
        var extension = new LocExtension("Title");

        Assert.Same(extension, extension.ProvideValue(new TargetProvider(new object(), TextBlock.TextProperty)));
    });

    private static TextBlock LocalizedText(LocalizationHost host)
    {
        var text = new TextBlock();
        text.Resources[LocalizationHost.ResourceKey] = host;
        text.Text = Assert.IsType<string>(new LocExtension("Title").ProvideValue(new TargetProvider(text, TextBlock.TextProperty)));
        return text;
    }

    private sealed class TargetProvider(object target, DependencyProperty property) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject => target;
        public object TargetProperty => property;
        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed class TestLocalizer(string prefix) : IStringLocalizer
    {
        private EventHandler? _changed;
        public int Subscribers { get; private set; }
        public event EventHandler? CultureChanged
        {
            add { _changed += value; Subscribers++; }
            remove { _changed -= value; Subscribers--; }
        }
        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
        public string Get(string key, params object[] args) => $"{prefix}:{key}";
        public void Change(string value) { prefix = value; _changed?.Invoke(this, EventArgs.Empty); }
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
