using System.Reflection;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.App;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public void Registers_one_shared_dictionary_lookup_for_single_and_batch_ports()
    {
        var services = new ServiceCollection();
        var configure = typeof(App).GetMethod(
            "ConfigureServices",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(configure);

        configure!.Invoke(null, [services]);
        using var provider = services.BuildServiceProvider();

        var single = provider.GetRequiredService<IDictionaryLookup>();
        var batch = provider.GetRequiredService<IBatchDictionaryLookup>();
        var resolver = provider.GetRequiredService<ITokenSpanResolver>();

        Assert.Same(single, batch);
        Assert.IsType<KotobaSenpai.Core.Services.TokenBoundarySpanResolver>(resolver);
    }
}
