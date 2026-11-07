using System.Reflection;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Workflow;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

public sealed class CompositionBoundaryTests
{
    [Fact]
    public void Wpf_entry_point_delegates_container_access_to_composition()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.App.App).Assembly).That().HaveName("App")
            .ShouldNot().HaveDependencyOnAny("Microsoft.Extensions.DependencyInjection", "System.IServiceProvider")
            .GetResult();

        Assert.True(result.IsSuccessful, "WPF startup must delegate object graph assembly to the composition root.");
    }

    [Fact]
    public void Startup_and_page_state_do_not_resolve_services_or_depend_on_the_container()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.App.App).Assembly).That()
            .ResideInNamespaceMatching(@"^KotobaSenpai\.App\.(Startup|ViewModels)(\..*)?$")
            .ShouldNot().HaveDependencyOnAny("Microsoft.Extensions.DependencyInjection", "System.IServiceProvider")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Runtime orchestration must receive typed dependencies: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [InlineData(typeof(RecognitionPageViewModel), typeof(RecognitionSessionController))]
    [InlineData(typeof(RecognitionPageViewModel), typeof(KotobaSenpai.App.Reading.ReadingActions))]
    [InlineData(typeof(SettingsPageViewModel), typeof(CaptureBorderPreferences))]
    [InlineData(typeof(SettingsPageViewModel), typeof(SentenceTranslationPreferences))]
    public void Page_dependencies_are_required_constructor_parameters(Type page, Type dependency)
    {
        var parameter = Assert.Single(page.GetConstructors()).GetParameters().Single(value => value.ParameterType == dependency);

        Assert.False(parameter.IsOptional);
    }

    [Fact]
    public void Localization_host_has_no_mutable_static_state()
    {
        var host = typeof(KotobaSenpai.App.App).Assembly.GetType("KotobaSenpai.App.Localization.LocalizationHost", throwOnError: true)!;

        Assert.DoesNotContain(host.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            field => !field.IsLiteral && !field.IsInitOnly);
        Assert.DoesNotContain(host.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            property => property.SetMethod is not null);
        Assert.True(typeof(IDisposable).IsAssignableFrom(host));
    }
}
