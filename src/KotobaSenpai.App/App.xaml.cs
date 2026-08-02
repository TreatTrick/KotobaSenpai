using System.IO;
using System.Windows;
using System.Windows.Threading;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Logging;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
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
/// 日志端口 <see cref="ILogger"/> 位于 Core，<see cref="FileLogger"/> 实现位于 App；启动时清理过期日志、
/// 注册全局未处理异常兜底（记日志后向用户显示本地化提示再受控退出），运行期靠跨日滚动事件触发清理。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private FileLogger? _logger;

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

        // 日志：启动即清理过期日志，存日志器供全局兜底（运行期靠跨日滚动事件触发清理，无后台定时器）。
        _services.GetRequiredService<LogRetentionPolicy>().Cleanup();
        _logger = _services.GetRequiredService<FileLogger>();

        ConfigureGlobalErrorHandling();

        var window = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            LanguageService = languageService
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Dispose();
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

        // 日志：FileLogger 单例持有文件句柄；LogRetentionPolicy 单例供启动/定时器/跨日滚动共用。
        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "logs");
        services.AddSingleton(_ => new LogRetentionPolicy(logsDirectory));
        services.AddSingleton(sp => new FileLogger(
            logsDirectory,
            LogConfiguration.LoadMinimumLevel(),
            sp.GetRequiredService<LogRetentionPolicy>()));
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<FileLogger>());

        services.AddSingleton<MainWindowViewModel>();
    }

    /// <summary>注册全局未处理异常兜底：均记 Error 级日志；终止性异常记日志后向用户显示本地化提示再退出。</summary>
    private void ConfigureGlobalErrorHandling()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 记日志（FileLogger 每次写入即刷新，崩溃前已落盘）后向用户提示，再受控退出。
        Try(() => _logger?.LogError(e.Exception, "Unhandled exception on dispatcher thread"));
        ShowUnexpectedErrorNotice();
        e.Handled = true;
        Shutdown(1);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Try(() => _logger?.LogError(ex, $"Unhandled AppDomain exception (terminating={e.IsTerminating})"));

        if (e.IsTerminating)
            Try(() => Application.Current?.Dispatcher.Invoke(ShowUnexpectedErrorNotice));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Try(() => _logger?.LogError(e.Exception, "Unobserved task exception"));
        // 不 SetObserved、不弹窗、不改变默认（非致命）语义。
    }

    /// <summary>显示通用本地化错误提示（不向用户暴露堆栈）。容器未就绪时跳过。</summary>
    private void ShowUnexpectedErrorNotice()
    {
        var localizer = _services?.GetService<IStringLocalizer>();
        if (localizer is null)
            return;

        var title = localizer.Get(ResourceKeys.UnexpectedError_Title);
        var message = localizer.Get(ResourceKeys.UnexpectedError_Message);
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>吞掉所有异常的简易包装，用于日志/兜底路径，确保这些路径绝不向调用方抛。</summary>
    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception) { }
    }
}
