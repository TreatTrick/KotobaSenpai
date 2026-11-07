using System.Reflection;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Presentation.Wpf.Overlay;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

public sealed class OverlaySeamArchitectureTests
{
    private static readonly Assembly Presentation = typeof(WpfOverlayRenderer).Assembly;

    [Theory]
    [InlineData("WpfOverlayRenderer")]
    [InlineData("OverlayWindow")]
    [InlineData("OverlayAnnotationLayer")]
    [InlineData("OverlayHoverController")]
    [InlineData("RegionSelectorWindow")]
    [InlineData("RegionSelectionVisuals")]
    public void Overlay_presentation_delegates_native_imports_to_the_windows_adapter(string name)
    {
        var module = Presentation.GetType($"KotobaSenpai.Presentation.Wpf.Overlay.{name}", throwOnError: true)!;
        AssertNoNativeImports(module);
    }

    [Fact]
    public void Target_tracking_orchestration_delegates_native_imports()
        => AssertNoNativeImports(typeof(KotobaSenpai.Platform.Windows.Overlay.WinEventTargetWindowTracker));

    [Theory]
    [InlineData("OverlayAnnotationLayer")]
    [InlineData("RegionSelectionVisuals")]
    public void Visual_modules_do_not_manage_native_windows_or_polling(string name)
    {
        var result = Types.InAssembly(Presentation).That().HaveName(name)
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.Platform.Windows", "System.Windows.Interop",
                "System.Windows.Threading", "System.Windows.Window")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Visual modules must remain independent of native hosting and polling: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Geometry_rules_belong_to_the_platform_independent_core()
    {
        var core = typeof(WindowTarget).Assembly;

        Assert.Same(core, typeof(RegionSelectionGeometry).Assembly);
        Assert.Same(core, typeof(RegionDragMode).Assembly);
        Assert.Same(core, typeof(WindowOcclusionGeometry).Assembly);
        Assert.Same(core, typeof(KotobaSenpai.Core.Overlay.WordHoverResolver).Assembly);
    }

    private static void AssertNoNativeImports(Type module)
    {
        var imports = module.Assembly.GetTypes()
            .Where(type => type == module || type.FullName?.StartsWith(module.FullName + "+", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<DllImportAttribute>() is not null)
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(imports);
    }
}
