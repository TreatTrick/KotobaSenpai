using System.Reflection;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Presentation.Wpf.Overlay;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

public sealed class OverlaySeamArchitectureTests
{
    private static readonly Assembly Presentation = typeof(WpfOverlayRenderer).Assembly;

    [Fact]
    public void Presentation_has_no_native_imports_at_all()
    {
        AssertNoNativeImports(Presentation.GetTypes());
    }

    [Fact]
    public void Target_tracking_orchestration_delegates_native_imports()
    {
        var tracker = typeof(KotobaSenpai.Platform.Windows.Overlay.WinEventTargetWindowTracker);
        AssertNoNativeImports(tracker.Assembly.GetTypes().Where(type => type == tracker
            || type.FullName?.StartsWith(tracker.FullName + "+", StringComparison.Ordinal) == true));
    }

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

    private static void AssertNoNativeImports(IEnumerable<Type> types)
    {
        var imports = types
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0
                || method.GetCustomAttribute<DllImportAttribute>() is not null
                || method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToArray();

        Assert.Empty(imports);
    }
}
