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

    /// <summary>本地化端口 IStringLocalizer 必须位于 Core，实现 ResourceManagerStringLocalizer 必须位于 App。</summary>
    [Fact]
    public void Localization_Port_ResidesInCore_Implementation_ResidesInApp()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        Assert.Same(coreAssembly, typeof(IStringLocalizer).Assembly);
        Assert.Same(appAssembly, typeof(ResourceManagerStringLocalizer).Assembly);
    }

    /// <summary>ViewModel 仅依赖 Core 的 IStringLocalizer 端口，不引用 App 实现 ResourceManagerStringLocalizer。</summary>
    [Fact]
    public void ViewModels_DependOnCoreLocalizationInterface_NotAppImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn(typeof(ResourceManagerStringLocalizer).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("ViewModels 不得依赖本地化实现 ResourceManagerStringLocalizer（应依赖 Core 端口）", result));
    }

    /// <summary>日志端口 ILogger/LogLevel 必须位于 Core，文件实现 FileLogger 必须位于 App。</summary>
    [Fact]
    public void Logging_Port_ResidesInCore_Implementation_ResidesInApp()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        Assert.Same(coreAssembly, typeof(ILogger).Assembly);
        Assert.Same(coreAssembly, typeof(LogLevel).Assembly);
        Assert.Same(appAssembly, typeof(FileLogger).Assembly);
    }

    /// <summary>ViewModel 仅依赖 Core 的 ILogger 端口，不引用 App 实现 FileLogger。</summary>
    [Fact]
    public void ViewModels_DependOnCoreLoggingInterface_NotAppImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn(typeof(FileLogger).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("ViewModels 不得依赖日志实现 FileLogger（应依赖 Core 端口）", result));
    }

    /// <summary>Wpf.Ui 程序集仅被 App 引用（视图层依赖），Core 与 Platform.Windows 不得引用。</summary>
    [Fact]
    public void WpfUi_Assembly_Referenced_OnlyBy_App()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var platformAssembly = typeof(KotobaSenpai.Platform.Windows.Overlay.WpfOverlayRenderer).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        static bool isWpfUi(string? name)
            => name is not null && name.StartsWith("Wpf.Ui", StringComparison.Ordinal);

        // App 引用了 Wpf.Ui（视图层 UI 库）。
        var appRefs = appAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.Contains(appRefs, isWpfUi);

        // Core 与 Platform.Windows 不得引用 Wpf.Ui。
        foreach (var assembly in new[] { coreAssembly, platformAssembly })
        {
            var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.DoesNotContain(refs, isWpfUi);
        }
    }

    /// <summary>ViewModel 不得依赖主题服务 MaterialThemeService（主题为视图层关切，不入 ViewModel）。</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_ThemeService()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.App.Themes")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels 不得依赖主题服务 MaterialThemeService（主题为视图层关切）", result));
    }

    /// <summary>设置端口 ISettingsService 必须位于 Core，文件实现 SettingsService 必须位于 App。</summary>
    [Fact]
    public void Settings_Port_ResidesInCore_Implementation_ResidesInApp()
    {
        var coreAssembly = typeof(KotobaSenpai.Core.Services.WordOverlayApplicationService).Assembly;
        var appAssembly = typeof(KotobaSenpai.App.App).Assembly;

        Assert.Same(coreAssembly, typeof(ISettingsService).Assembly);
        Assert.Same(appAssembly, typeof(SettingsService).Assembly);
    }

    /// <summary>偏好存储与日志级别配置须经 ISettingsService 端口存取设置，不直接读写文件。</summary>
    [Fact]
    public void Settings_Consumers_DependOnCoreSettingsPort()
    {
        // 偏好存储经构造函数注入 ISettingsService，不再经静态助手直接读写文件。
        var languageStoreCtor = typeof(LocalAppDataLanguagePreferenceStore).GetConstructors().Single();
        Assert.Contains(languageStoreCtor.GetParameters(), p => p.ParameterType == typeof(ISettingsService));

        var themeStoreCtor = typeof(LocalAppDataThemePreferenceStore).GetConstructors().Single();
        Assert.Contains(themeStoreCtor.GetParameters(), p => p.ParameterType == typeof(ISettingsService));

        // LogConfiguration 经方法参数接收 ISettingsService（不再自带文件路径/读取逻辑）。
        var loadMinLevel = typeof(LogConfiguration).GetMethod(nameof(LogConfiguration.LoadMinimumLevel));
        Assert.NotNull(loadMinLevel);
        Assert.Contains(loadMinLevel!.GetParameters(), p => p.ParameterType == typeof(ISettingsService));
    }

    /// <summary>ViewModel 不得依赖设置文件实现（设置实现位于 App 基础设施层，ViewModel 须经端口）。</summary>
    [Fact]
    public void ViewModels_ShouldNotDependOn_SettingsImplementation()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceMatching(ViewModelsNamespace)
            .ShouldNot().HaveDependencyOn("KotobaSenpai.App.Settings")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("App.ViewModels 不得依赖设置实现 SettingsService（设置实现位于 App 基础设施层）", result));
    }

    private static string FormatFailure(string rule, TestResult result) =>
        result.IsSuccessful
            ? rule
            : $"{rule}。违规类型：{string.Join(", ", result.FailingTypeNames)}";
}
