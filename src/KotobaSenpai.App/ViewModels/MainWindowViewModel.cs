using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.App.ViewModels;

/// <summary>
/// 主窗口视图模型：编排窗口选择、识别和隐藏用例。
/// 仅依赖 Core 端口与应用服务（含本地化端口 <see cref="IStringLocalizer"/> 与
/// <see cref="IUserMessageResolver"/>），不引用 WPF 或平台实现，因此可在无桌面的测试中验证。
/// 所有用户可见文案经 <see cref="IStringLocalizer"/> 解析；文化切换时按当前状态重算 <see cref="Status"/>。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IWindowCatalog _catalog;
    private readonly WordOverlayApplicationService _workflow;
    private readonly IStringLocalizer _localizer;
    private readonly IUserMessageResolver _messageResolver;
    private readonly ILogger _logger;

    /// <summary>按当前状态渲染本地化 Status 的委托；文化切换时重新调用以就地刷新。</summary>
    private Func<string> _renderStatus = () => string.Empty;

    public MainWindowViewModel(
        IWindowCatalog catalog,
        WordOverlayApplicationService workflow,
        IStringLocalizer localizer,
        IUserMessageResolver messageResolver,
        ILogger logger)
    {
        _catalog = catalog;
        _workflow = workflow;
        _localizer = localizer;
        _messageResolver = messageResolver;
        _logger = logger;

        _localizer.CultureChanged += OnCultureChanged;
        SetStatus(ResourceKeys.Status_SelectTarget);
    }

    /// <summary>可选目标窗口列表。</summary>
    public ObservableCollection<WindowTarget> Windows { get; } = new();

    /// <summary>当前选中的目标窗口。</summary>
    [ObservableProperty]
    private WindowTarget? _selectedWindow;

    /// <summary>用户可见的状态文本。</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// 主窗口自身的句柄，用于从候选列表中排除自己。
    /// 由视图在窗口句柄创建后设置（HWND 属于平台细节，不应下沉到领域）。
    /// </summary>
    public nint ExcludeHandle { get; set; }

    /// <summary>选择窗口后更新状态提示。</summary>
    partial void OnSelectedWindowChanged(WindowTarget? value)
        => SetStatus(value is null ? ResourceKeys.Status_SelectTarget : ResourceKeys.Status_Selected,
                     value?.Title ?? string.Empty);

    /// <summary>重新枚举可见窗口并刷新候选列表。</summary>
    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var previousHandle = SelectedWindow?.Handle;
            var current = _catalog.ListVisibleWindows()
                .Where(window => window.Handle != ExcludeHandle)
                .OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            Windows.Clear();
            foreach (var window in current) Windows.Add(window);
            SelectedWindow = Windows.FirstOrDefault(window => window.Handle == previousHandle)
                ?? Windows.FirstOrDefault();
            SetStatus(Windows.Count == 0 ? ResourceKeys.Status_NoWindows : ResourceKeys.Status_WindowsFound,
                      Windows.Count);
        }
        catch (Exception ex)
        {
            SetError(ex, ErrorCodes.WindowEnumerationFailed);
        }
    }

    /// <summary>捕获并识别当前窗口的日语词，绘制下划线。</summary>
    [RelayCommand]
    private async Task RecognizeAsync()
    {
        if (SelectedWindow is null)
        {
            SetStatus(ResourceKeys.Status_SelectTargetFirst);
            return;
        }

        try
        {
            SetStatus(ResourceKeys.Status_Recognizing);
            var result = await _workflow.RecognizeAndShowAsync(SelectedWindow);
            SetStatus(result.Words.Count == 0
                ? ResourceKeys.Status_NoWords
                : ResourceKeys.Status_WordsRecognized,
                result.Words.Count);
        }
        catch (Exception ex)
        {
            _workflow.Hide();
            SetError(ex, ErrorCodes.RecognitionFailed);
        }
    }

    /// <summary>隐藏下划线覆盖层。</summary>
    [RelayCommand]
    private void Hide()
    {
        _workflow.Hide();
        SetStatus(ResourceKeys.Status_Hidden);
    }

    /// <summary>记录普通状态（键 + 格式参数）并渲染。</summary>
    private void SetStatus(string key, params object[] args)
    {
        _renderStatus = () => _localizer.Get(key, args);
        Status = _renderStatus();
    }

    /// <summary>记录错误状态（异常 + 回退码）并经解析器渲染；文化切换时按码重新翻译。</summary>
    private void SetError(Exception exception, string fallbackErrorCode)
    {
        _logger.LogError(exception, "Error reported to user");
        _renderStatus = () => _messageResolver.Resolve(exception, fallbackErrorCode);
        Status = _renderStatus();
    }

    /// <summary>文化切换时按当前状态重新派生 <see cref="Status"/> 并通知视图。</summary>
    private void OnCultureChanged(object? sender, EventArgs e)
        => Status = _renderStatus();
}
