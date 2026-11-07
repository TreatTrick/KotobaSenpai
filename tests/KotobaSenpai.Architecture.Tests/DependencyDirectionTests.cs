using KotobaSenpai.Core.Workflow;
using System.Reflection;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
using KotobaSenpai.Platform.Windows.Settings;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using NetArchTest.Rules;
using KotobaSenpai.Presentation.Wpf.Overlay;

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
    /// <summary>The four assemblies under check, marked by a public type from each project so the Assembly is resolved stably.</summary>
    private static readonly Assembly[] Assemblies =
    [
        typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly,       // Core
        typeof(KotobaSenpai.Platform.Windows.Settings.SettingsService).Assembly,          // Platform.Windows
        typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup).Assembly,               // Presentation.Wpf
        typeof(KotobaSenpai.App.App).Assembly,                                            // App
    ];

    private const string CoreNamespace = @"KotobaSenpai\.Core(\..*)?$";
    private const string PlatformNamespace = @"KotobaSenpai\.Platform\.Windows(\..*)?$";
    private const string PresentationNamespace = @"KotobaSenpai\.Presentation\.Wpf(\..*)?$";
    private const string ViewModelsNamespace = @"KotobaSenpai\.App\.ViewModels(\..*)?$";

    [Fact]
    public void Guidance_flow_has_no_presentation_platform_or_container_dependencies()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.App.Guidance.ContextualGuidance).Assembly)
            .That().ResideInNamespaceMatching(@"^KotobaSenpai\.App\.Guidance$")
            .ShouldNot().HaveDependencyOnAny("System.Windows", "KotobaSenpai.Platform.Windows",
                "KotobaSenpai.App.Views", "KotobaSenpai.App.ViewModels", "Microsoft.Extensions.DependencyInjection")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure("Guidance flow must remain independent of presentation and the container", result));
    }

    [Fact]
    public void Onboarding_state_machine_has_no_presentation_platform_or_container_dependencies()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.App.Onboarding.OnboardingStateMachine).Assembly)
            .That().ResideInNamespace("KotobaSenpai.App.Onboarding")
            .ShouldNot().HaveDependencyOnAny("System.Windows", "KotobaSenpai.Platform.Windows",
                "KotobaSenpai.App.Views", "KotobaSenpai.App.ViewModels", "Microsoft.Extensions.DependencyInjection", "System.IServiceProvider")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure("Onboarding flow must remain independent of presentation and the container", result));
    }

    [Fact]
    public void Settings_batch_operations_require_an_explicit_implementation()
    {
        Assert.True(typeof(ISettingsService).GetMethod(nameof(ISettingsService.GetSnapshot))!.IsAbstract);
        Assert.True(typeof(ISettingsService).GetMethod(nameof(ISettingsService.TryUpdate))!.IsAbstract);
    }

    /// <summary>Core must not depend on Platform.Windows or App (pure domain, zero external dependencies).</summary>
    [Fact]
    public void Agent_cli_uses_shared_infrastructure_without_implementing_provider_requests_or_file_access()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.Cli.Program).Assembly)
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.App", "System.Windows", "System.Net.Http.HttpRequestMessage", "System.IO.File", "System.IO.Directory")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure("CLI must delegate provider and settings operations", result));
    }

    [Fact]
    public void Agent_cli_calls_the_shared_configuration_use_case_directly()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.Cli.Program).Assembly)
            .That().HaveName("Program")
            .Should().HaveDependencyOn("KotobaSenpai.Core.Llm.LlmConfigurationService")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure("CLI must execute the shared configuration use case in its own process", result));
    }

    [Fact]
    public void Core_remains_free_of_http_wpf_and_file_system_dependencies()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.Core.Llm.LlmConfigurationService).Assembly)
            .ShouldNot().HaveDependencyOnAny("System.Net.Http", "System.Windows", "System.IO")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure("Core must remain independent of technical adapters", result));
    }

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
        var coreAssembly = typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly;
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
        var coreAssembly = typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly;
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

    /// <summary>The Wpf.Ui assembly is referenced only by App (a view-layer dependency); Core, Platform.Windows and Presentation.Wpf must not reference it.</summary>
    [Fact]
    public void WpfUi_Assembly_Referenced_OnlyBy_App()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly;
        var platformAssembly = typeof(KotobaSenpai.Platform.Windows.Settings.SettingsService).Assembly;
        var presentationAssembly = typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        static bool isWpfUi(string? name)
            => name is not null && name.StartsWith("Wpf.Ui", StringComparison.Ordinal);

        // App references Wpf.Ui (a view-layer UI library).
        var appRefs = appAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.Contains(appRefs, isWpfUi);

        // Core, Platform.Windows and Presentation.Wpf must not reference Wpf.Ui.
        foreach (var assembly in new[] { coreAssembly, platformAssembly, presentationAssembly })
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

    /// <summary>The settings port lives in Core; its reusable file adapter belongs to Windows infrastructure.</summary>
    [Fact]
    public void Settings_port_resides_in_core_and_implementation_resides_in_windows_infrastructure()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly;
        var infrastructureAssembly = typeof(KotobaSenpai.Platform.Windows.Llm.LlmPhraseAnalyzer).Assembly;

        Assert.Same(coreAssembly, typeof(ISettingsService).Assembly);
        Assert.Same(infrastructureAssembly, typeof(SettingsService).Assembly);
    }

    [Fact]
    public void Llm_model_catalog_port_resides_in_core()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly;
        Assert.Same(coreAssembly, typeof(KotobaSenpai.Core.Llm.ILlmModelCatalog).Assembly);
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

    /// <summary>ViewModels must access settings through the Core port rather than the file adapter.</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_SettingsImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.Platform.Windows.Settings")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels must depend on the Core settings port", result));
    }

    // ---------------------------------------------------------------------------------------------
    // Presentation boundary: WPF surfaces live in KotobaSenpai.Presentation.Wpf, never in the
    // platform adapter. These rules exist because the adapter previously shipped PhrasePopup.xaml,
    // GuidanceOverlay.xaml and the design tokens, and owned a contract (ITourPresentation) that only
    // the App implemented — the dependency direction was inverted at the type level.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Platform.Windows is an adapter: it must not know about the WPF presentation assembly.</summary>
    [Fact]
    public void Platform_must_not_reference_the_presentation_assembly()
    {
        var platformAssembly = typeof(KotobaSenpai.Platform.Windows.Settings.SettingsService).Assembly;
        var references = platformAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("KotobaSenpai.Presentation.Wpf", references);
    }

    /// <summary>Presentation.Wpf is below the composition root; it must not depend on App.</summary>
    [Fact]
    public void Presentation_must_not_reference_the_app_assembly()
    {
        var presentationAssembly = typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup).Assembly;
        var references = presentationAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("KotobaSenpai.App", references);
    }

    /// <summary>All WPF surfaces resolve to the presentation assembly, and the adapter exposes none.</summary>
    [Fact]
    public void Wpf_surfaces_reside_in_the_presentation_assembly()
    {
        var presentationAssembly = typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup).Assembly;
        var platformAssembly = typeof(KotobaSenpai.Platform.Windows.Settings.SettingsService).Assembly;

        foreach (var surface in new[]
                 {
                     typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup),
                     typeof(KotobaSenpai.Presentation.Wpf.Overlay.WpfOverlayRenderer),
                     typeof(KotobaSenpai.Presentation.Wpf.Overlay.RegionSelectorWindow),
                     typeof(KotobaSenpai.Presentation.Wpf.Overlay.DesktopOverlayWindow),
                     typeof(KotobaSenpai.Presentation.Wpf.Overlay.OverlayAnnotation),
                     typeof(KotobaSenpai.Presentation.Wpf.Guidance.GuidanceOverlay),
                     typeof(KotobaSenpai.Presentation.Wpf.Resources.ThemeResourceKeys),
                 })
        {
            Assert.Same(presentationAssembly, surface.Assembly);
        }

        Assert.True(typeof(KotobaSenpai.Presentation.Wpf.Overlay.RegionSelectorWindow)
            .IsSubclassOf(typeof(System.Windows.Window)));

        var visualTypes = platformAssembly.GetTypes()
            .Where(type => typeof(System.Windows.FrameworkElement).IsAssignableFrom(type)
                || typeof(System.Windows.Window).IsAssignableFrom(type)
                || typeof(System.Windows.Controls.Control).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToArray();
        Assert.True(visualTypes.Length == 0,
            $"Platform.Windows must not expose WPF visual types: {string.Join(", ", visualTypes)}");
    }

    /// <summary>The adapter must not compile XAML; a page or resource dictionary there is a presentation leak.</summary>
    [Fact]
    public void Platform_must_not_embed_xaml_resources()
    {
        var platformAssembly = typeof(KotobaSenpai.Platform.Windows.Settings.SettingsService).Assembly;
        var presentationAssembly = typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup).Assembly;

        Assert.Contains(presentationAssembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(platformAssembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Remaining WPF usage in the adapter is threading glue only: no controls, media or shapes.</summary>
    [Fact]
    public void Platform_wpf_usage_is_limited_to_threading_and_interop()
    {
        var result = Types.InAssembly(typeof(KotobaSenpai.Platform.Windows.Settings.SettingsService).Assembly)
            .ShouldNot().HaveDependencyOnAny("System.Windows.Controls", "System.Windows.Media",
                "System.Windows.Shapes", "System.Windows.Data", "System.Windows.Documents")
            .GetResult();

        Assert.True(result.IsSuccessful,
            FormatFailure("Platform.Windows must not depend on WPF visual namespaces", result));
    }

    /// <summary>
    /// The guided-tour contract is consumed by the presentation assembly and implemented by the
    /// composition root, so it is owned by presentation — not by the adapter it used to live in.
    /// </summary>
    [Fact]
    public void Guided_tour_contract_is_owned_by_presentation_and_implemented_by_app()
    {
        var presentationAssembly = typeof(KotobaSenpai.Presentation.Wpf.Overlay.PhrasePopup).Assembly;
        var contract = typeof(KotobaSenpai.Presentation.Wpf.Guidance.ITourPresentation);
        Assert.Same(presentationAssembly, contract.Assembly);

        var implementation = typeof(KotobaSenpai.App.Guidance.Presentation.TourPresentation);
        Assert.Contains(contract, implementation.GetInterfaces());
        Assert.NotSame(presentationAssembly, implementation.Assembly);
    }

    private static string FormatFailure(string rule, TestResult result) =>
        result.IsSuccessful
            ? rule
            : $"{rule}. Violating types: {string.Join(", ", result.FailingTypeNames)}";
}
