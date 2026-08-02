using System.Windows;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Services;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Platform.Windows.Ocr;
using KotobaSenpai.Platform.Windows.Overlay;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App;

/// <summary>
/// 组合根：启动时按端口注册依赖并由容器装配对象图。
/// 平台适配器实现领域端口，应用服务与视图模型由容器自动解析其构造依赖。
/// 本地化端口（<see cref="IStringLocalizer"/> 等）位于 Core，实现位于 App；启动时在任何本地化资源
/// 被访问前由 <see cref="LanguageService"/> 解析并设置 UI 文化。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // 在任何本地化资源被访问前，按偏好/OS 解析并设置 UI 文化。
        var languageService = _services.GetRequiredService<LanguageService>();
        languageService.Initialize();

        // 供 XAML LocExtension 静态桥解析键。
        LocalizationHost.Localizer = _services.GetRequiredService<IStringLocalizer>();

        var window = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            LanguageService = languageService
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>按端口注册领域端口与其平台适配器；本地化端口与语言服务由容器解析依赖。</summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWindowCatalog, Win32WindowCatalog>();
        services.AddSingleton<IWindowFrameCapture, GdiWindowFrameCapture>();
        services.AddSingleton<IWindowWordRecognizer, WindowsOcrWordRecognizer>();
        services.AddSingleton<IOverlayRenderer, WpfOverlayRenderer>();
        services.AddSingleton<WordOverlayApplicationService>();

        // 本地化：具体实现与 Core 端口指向同一单例，使 LanguageService 与 ViewModel 共享文化状态。
        services.AddSingleton<ResourceManagerStringLocalizer>();
        services.AddSingleton<IStringLocalizer>(sp => sp.GetRequiredService<ResourceManagerStringLocalizer>());
        services.AddSingleton<IUserMessageResolver, UserMessageResolver>();
        services.AddSingleton<ILanguagePreferenceStore, LocalAppDataLanguagePreferenceStore>();
        services.AddSingleton<LanguageService>();

        services.AddSingleton<MainWindowViewModel>();
    }
}
