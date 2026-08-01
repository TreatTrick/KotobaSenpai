using System.Windows;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Services;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Platform.Windows.Ocr;
using KotobaSenpai.Platform.Windows.Overlay;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App;

/// <summary>
/// 组合根：启动时按端口注册依赖并由容器装配对象图。
/// 平台适配器实现领域端口，应用服务与视图模型由容器自动解析其构造依赖。
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

        var window = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainWindowViewModel>()
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>按端口注册领域端口与其平台适配器；应用服务、视图模型由容器解析依赖。</summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWindowCatalog, Win32WindowCatalog>();
        services.AddSingleton<IWindowFrameCapture, GdiWindowFrameCapture>();
        services.AddSingleton<IWindowWordRecognizer, WindowsOcrWordRecognizer>();
        services.AddSingleton<IOverlayRenderer, WpfOverlayRenderer>();
        services.AddSingleton<WordOverlayApplicationService>();
        services.AddSingleton<MainWindowViewModel>();
    }
}
