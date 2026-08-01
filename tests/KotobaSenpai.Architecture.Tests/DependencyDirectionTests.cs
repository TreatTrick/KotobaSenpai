using System.Reflection;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

/// <summary>
/// 依赖方向架构测试：用 NetArchTest 把 Core / Platform.Windows / App 的依赖方向钉死，
/// 等价于 ESLint 的 import 规则，随 <c>dotnet test</c> / CI 执行。
/// <para>
/// 规则依据端口适配器（六边形）架构与组合根边界：
/// <list type="bullet">
/// <item>Core 是纯领域，零外部依赖；</item>
/// <item>Platform.Windows 是适配器，只往内指 Core，禁止反向引用 App；</item>
/// <item>App 是组合根，ViewModel 仅依赖 Core 端口与应用服务，不碰 WPF / 平台实现。</item>
/// </list>
/// </para>
/// </summary>
public sealed class DependencyDirectionTests
{
    /// <summary>纳入检查的三个程序集，用各项目的公共类型作标记以稳定取到 Assembly。</summary>
    private static readonly Assembly[] Assemblies =
    [
        typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly, // Core
        typeof(KotobaSenpai.Platform.Windows.Overlay.WpfOverlayRenderer).Assembly, // Platform.Windows
        typeof(KotobaSenpai.App.App).Assembly,                                      // App
    ];

    private const string CoreNamespace = @"KotobaSenpai\.Core(\..*)?$";
    private const string PlatformNamespace = @"KotobaSenpai\.Platform\.Windows(\..*)?$";
    private const string ViewModelsNamespace = @"KotobaSenpai\.App\.ViewModels(\..*)?$";

    /// <summary>Core 不得依赖 Platform.Windows 或 App（纯领域，零外部依赖）。</summary>
    [Fact]
    public void Core_ShouldNotDependOn_Platform_Or_App()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(CoreNamespace)
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.Platform.Windows", "KotobaSenpai.App")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Core 不得依赖 Platform.Windows / App", result));
    }

    /// <summary>Platform.Windows 不得依赖 App（适配器只能往内指 Core）。</summary>
    [Fact]
    public void Platform_ShouldNotDependOn_App()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(PlatformNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.App")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Platform.Windows 不得依赖 App", result));
    }

    /// <summary>ViewModel 仅依赖 Core 端口与应用服务，不得引用平台实现或 WPF。</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_Platform_Or_Wpf()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOnAny("KotobaSenpai.Platform.Windows", "System.Windows")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels 不得依赖 Platform.Windows / WPF", result));
    }

    private static string FormatFailure(string rule, TestResult result) =>
        result.IsSuccessful
            ? rule
            : $"{rule}。违规类型：{string.Join(", ", result.FailingTypeNames)}";
}
