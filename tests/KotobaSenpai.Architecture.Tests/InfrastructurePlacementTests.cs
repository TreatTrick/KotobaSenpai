using KotobaSenpai.App.Japanese;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Platform.Windows.Japanese;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

public sealed class InfrastructurePlacementTests
{
    [Fact]
    public void Dictionary_installation_contracts_reside_in_core_and_implementation_resides_in_windows()
    {
        var core = typeof(UniDicAssets).Assembly;
        var platform = typeof(UniDicTokenizer).Assembly;
        var app = typeof(UniDicInstallController).Assembly;

        Assert.Same(core, typeof(IUniDicDictionaryInstaller).Assembly);
        Assert.Same(core, typeof(DictionaryInstallStage).Assembly);
        Assert.Same(core, typeof(DictionaryInstallProgress).Assembly);
        Assert.Same(platform, typeof(UniDicDictionaryInstaller).Assembly);
        Assert.True(typeof(IUniDicDictionaryInstaller).IsAssignableFrom(typeof(UniDicDictionaryInstaller)));
        Assert.DoesNotContain(app.GetTypes(), type => type.Name == nameof(UniDicDictionaryInstaller));
        Assert.Contains(Assert.Single(typeof(UniDicInstallController).GetConstructors()).GetParameters(),
            parameter => parameter.ParameterType == typeof(IUniDicDictionaryInstaller) && !parameter.IsOptional);
    }

    [Fact]
    public void App_dictionary_coordination_does_not_implement_or_depend_on_infrastructure()
    {
        var result = Types.InAssembly(typeof(UniDicInstallController).Assembly).That()
            .ResideInNamespaceMatching(@"^KotobaSenpai\.App\.Japanese(\..*)?$")
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.Platform.Windows", "KotobaSenpai.Presentation.Wpf",
                "System.IO", "System.Net.Http", "System.Security.Cryptography", "System.Threading.Mutex",
                "System.Threading.SemaphoreSlim")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Dictionary coordination must use Core ports instead of infrastructure: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Only_the_app_composition_root_may_reference_the_dictionary_installer_implementation()
    {
        var result = Types.InAssembly(typeof(UniDicInstallController).Assembly).That()
            .ResideInNamespaceMatching(@"^KotobaSenpai\.App(?:\.(?!Composition(?:\.|$)).*)?$")
            .ShouldNot().HaveDependencyOn(typeof(UniDicDictionaryInstaller).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Only composition may reference the concrete dictionary installer: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Dictionary_infrastructure_does_not_depend_on_WPF_or_page_state()
    {
        var result = Types.InAssembly(typeof(UniDicDictionaryInstaller).Assembly).That()
            .ResideInNamespaceMatching(@"^KotobaSenpai\.Platform\.Windows\.Japanese(\..*)?$")
            .ShouldNot().HaveDependencyOnAny("System.Windows", "CommunityToolkit.Mvvm",
                "KotobaSenpai.Presentation.Wpf", "KotobaSenpai.App")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Dictionary infrastructure must be independent of presentation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
