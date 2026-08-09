using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Japanese;

/// <summary>
/// 词典安装协调器：驱动 <see cref="UniDicDictionaryInstaller"/> 在启动时执行安装，
/// 暴露进度/错误状态供主窗口遮挡层绑定。安装期间遮挡层阻断所有其他操作，失败时在层内显示错误并允许重试。
/// <paramref name="install"/> 可注入（测试用），默认委托给安装器的 <see cref="UniDicDictionaryInstaller.EnsureInstalledAsync"/>。
/// </summary>
public sealed partial class UniDicInstallController : ObservableObject
{
    private readonly IUserMessageResolver _messageResolver;
    private readonly Func<CancellationToken, IProgress<double>, Task> _install;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking))]
    private bool _isInstalling;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking), nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _isInstalled;

    public UniDicInstallController(
        UniDicDictionaryInstaller installer,
        IUserMessageResolver messageResolver,
        Func<CancellationToken, IProgress<double>, Task>? install = null)
    {
        ArgumentNullException.ThrowIfNull(installer);
        _messageResolver = messageResolver ?? throw new ArgumentNullException(nameof(messageResolver));
        _install = install ?? ((ct, p) => installer.EnsureInstalledAsync(p, ct));
        IsInstalled = installer.IsInstalled;
    }

    /// <summary>遮挡层可见（阻断操作）：正在安装或已出错。</summary>
    public bool IsBlocking => IsInstalling || HasError;

    /// <summary>安装失败后有可展示的错误信息。</summary>
    public bool HasError => Error is not null;

    /// <summary>执行安装；已安装则直接返回。成功后收起遮挡层，失败时在层内显示本地化错误。</summary>
    [RelayCommand]
    private async Task InstallAsync(CancellationToken ct = default)
    {
        if (IsInstalled)
            return;

        IsInstalling = true;
        Error = null;
        Progress = 0;
        try
        {
            await _install(ct, new Progress<double>(p => Progress = p));
            IsInstalled = true;
        }
        catch (OperationCanceledException)
        {
            // 取消：保持遮挡层可见，交由用户决定重试或关窗。
        }
        catch (Exception ex)
        {
            Error = _messageResolver.Resolve(ex, ErrorCodes.UniDicDownloadFailed);
        }
        finally
        {
            IsInstalling = false;
        }
    }
}