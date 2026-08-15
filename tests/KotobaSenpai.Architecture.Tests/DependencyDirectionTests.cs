using System.Reflection;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
using KotobaSenpai.App.Settings;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

/// <summary>
/// Dependency-direction architecture tests: NetArchTest pins down the dependency direction of Core / Platform.Windows / App,
/// equivalent to ESLint import rules, run as part of <c>dotnet test</c> / CI.
/// <para>
/// The rules follow the ports-and-adapters (hexagonal) architecture and composition-root boundaries:
/// <list type="bullet">
/// <item>Core is pure domain with zero external dependencies;</item>
/// <item>Platform.Windows is an adapter pointing inward at Core only, and must not reference App;</item>
/// <item>App is the composition root; ViewModels depend only on Core ports and application services, not WPF / platform implementations.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DependencyDirectionTests
{
    /// <summary>The three assemblies under check, marked by a public type from each project so the Assembly is resolved stably.</summary>
    private static readonly Assembly[] Assemblies =
    [
        typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly, // Core
        typeof(KotobaSenpai.Platform.Windows.Overlay.WpfOverlayRenderer).Assembly, // Platform.Windows
        typeof(KotobaSenpai.App.App).Assembly,                                      // App
    ];

    private const string CoreNamespace = @"KotobaSenpai\.Core(\..*)?$";
    private const string PlatformNamespace = @"KotobaSenpai\.Platform\.Windows(\..*)?$";
    private const string ViewModelsNamespace = @"KotobaSenpai\.App\.ViewModels(\..*)?$";

    /// <summary>Core must not depend on Platform.Windows or App (pure domain, zero external dependencies).</summary>
    [Fact]
    public void Core_ShouldNotDependOn_Platform_Or_App()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(CoreNamespace)
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.Platform.Windows", "KotobaSenpai.App")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Core must not depend on Platform.Windows / App", result));
    }

    /// <summary>Platform.Windows must not depend on App (an adapter only points inward at Core).</summary>
    [Fact]
    public void Platform_ShouldNotDependOn_App()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(PlatformNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.App")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Platform.Windows must not depend on App", result));
    }

    /// <summary>ViewModels depend only on Core ports and application services; they must not reference platform implementations or WPF.</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_Platform_Or_Wpf()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.Platform.Windows", "System.Windows")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels must not depend on Platform.Windows / WPF", result));
    }

    /// <summary>The localization port IStringLocalizer must live in Core; its implementation ResourceManagerStringLocalizer must live in App.</summary>
    [Fact]
    public void Localization_Port_ResidesInCore_Implementation_ResidesInApp()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        Assert.Same(coreAssembly, typeof(IStringLocalizer).Assembly);
        Assert.Same(appAssembly, typeof(ResourceManagerStringLocalizer).Assembly);
    }

    /// <summary>ViewModels depend only on Core's IStringLocalizer port, not the App implementation ResourceManagerStringLocalizer.</summary>
    [Fact]
    public void ViewModels_DependOnCoreLocalizationInterface_NotAppImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn(typeof(ResourceManagerStringLocalizer).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("ViewModels must not depend on the localization implementation ResourceManagerStringLocalizer (should depend on the Core port)", result));
    }

    /// <summary>The logging port ILogger/LogLevel must live in Core; the file implementation FileLogger must live in App.</summary>
    [Fact]
    public void Logging_Port_ResidesInCore_Implementation_ResidesInApp()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        Assert.Same(coreAssembly, typeof(ILogger).Assembly);
        Assert.Same(coreAssembly, typeof(LogLevel).Assembly);
        Assert.Same(appAssembly, typeof(FileLogger).Assembly);
    }

    /// <summary>ViewModels depend only on Core's ILogger port, not the App implementation FileLogger.</summary>
    [Fact]
    public void ViewModels_DependOnCoreLoggingInterface_NotAppImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn(typeof(FileLogger).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("ViewModels must not depend on the logging implementation FileLogger (should depend on the Core port)", result));
    }

    /// <summary>The Wpf.Ui assembly is referenced only by App (a view-layer dependency); Core and Platform.Windows must not reference it.</summary>
    [Fact]
    public void WpfUi_Assembly_Referenced_OnlyBy_App()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var platformAssembly = typeof(KotobaSenpai.Platform.Windows.Overlay.WpfOverlayRenderer).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        static bool isWpfUi(string? name)
            => name is not null && name.StartsWith("Wpf.Ui", StringComparison.Ordinal);

        // App references Wpf.Ui (a view-layer UI library).
        var appRefs = appAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.Contains(appRefs, isWpfUi);

        // Core and Platform.Windows must not reference Wpf.Ui.
        foreach (var assembly in new[] { coreAssembly, platformAssembly })
        {
            var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.DoesNotContain(refs, isWpfUi);
        }
    }

    /// <summary>ViewModels must not depend on the theme service MaterialThemeService (theme is a view-layer concern, not for ViewModels).</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_ThemeService()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.App.Themes")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels must not depend on the theme service MaterialThemeService (theme is a view-layer concern)", result));
    }

    /// <summary>The settings port ISettingsService must live in Core; the file implementation SettingsService must live in App.</summary>
    [Fact]
    public void Settings_Port_ResidesInCore_Implementation_ResidesInApp()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        Assert.Same(coreAssembly, typeof(ISettingsService).Assembly);
        Assert.Same(appAssembly, typeof(SettingsService).Assembly);
    }

    /// <summary>Preference stores and log-level configuration must access settings through the ISettingsService port, not read/write files directly.</summary>
    [Fact]
    public void Settings_Consumers_DependOnCoreSettingsPort()
    {
        // Preference stores receive ISettingsService via constructor injection, no longer reading/writing files via static helpers.
        var languageStoreCtor = typeof(LocalAppDataLanguagePreferenceStore).GetConstructors().Single();
        Assert.Contains(languageStoreCtor.GetParameters(), p => p.ParameterType == typeof(ISettingsService));

        var themeStoreCtor = typeof(LocalAppDataThemePreferenceStore).GetConstructors().Single();
        Assert.Contains(themeStoreCtor.GetParameters(), p => p.ParameterType == typeof(ISettingsService));

        // LogConfiguration receives ISettingsService via method parameter (no longer carries its own file path/reading logic).
        var loadMinLevel = typeof(LogConfiguration).GetMethod(nameof(LogConfiguration.LoadMinimumLevel));
        Assert.NotNull(loadMinLevel);
        Assert.Contains(loadMinLevel!.GetParameters(), p => p.ParameterType == typeof(ISettingsService));
    }

    /// <summary>ViewModels must not depend on the settings file implementation (settings implementation lives in the App infrastructure layer; ViewModels must go through the port).</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_SettingsImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.App.Settings")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels must not depend on the settings implementation SettingsService (settings implementation lives in the App infrastructure layer)", result));
    }

    private static string FormatFailure(string rule, TestResult result) =>
        result.IsSuccessful
            ? rule
            : $"{rule}. Violating types: {string.Join(", ", result.FailingTypeNames)}";
}
