using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.App.ViewModels;

/// <summary>
/// 主窗口视图模型：编排窗口选择、识别和隐藏用例。
/// 仅依赖 Core 端口与应用服务，不引用 WPF，因此可在无桌面的测试中验证。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IWindowCatalog _catalog;
    private readonly WordOverlayApplicationService _workflow;

    public MainWindowViewModel(IWindowCatalog catalog, WordOverlayApplicationService workflow)
    {
        _catalog = catalog;
        _workflow = workflow;
    }

    /// <summary>可选目标窗口列表。</summary>
    public ObservableCollection<WindowTarget> Windows { get; } = new();

    /// <summary>当前选中的目标窗口。</summary>
    [ObservableProperty]
    private WindowTarget? _selectedWindow;

    /// <summary>用户可见的状态文本。</summary>
    [ObservableProperty]
    private string _status = "请选择目标窗口。";

    /// <summary>
    /// 主窗口自身的句柄，用于从候选列表中排除自己。
    /// 由视图在窗口句柄创建后设置（HWND 属于平台细节，不应下沉到领域）。
    /// </summary>
    public nint ExcludeHandle { get; set; }

    /// <summary>选择窗口后更新状态提示。</summary>
    partial void OnSelectedWindowChanged(WindowTarget? value)
        => Status = value is null ? "请选择目标窗口。" : $"已选择：{value.Title}";

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
            Status = Windows.Count == 0 ? "没有找到可用的窗口。" : $"找到 {Windows.Count} 个窗口，请选择目标。";
        }
        catch (Exception ex)
        {
            Status = $"窗口枚举失败：{ex.Message}";
        }
    }

    /// <summary>捕获并识别当前窗口的日语词，绘制下划线。</summary>
    [RelayCommand]
    private async Task RecognizeAsync()
    {
        if (SelectedWindow is null)
        {
            Status = "请先选择目标窗口。";
            return;
        }

        try
        {
            Status = "正在捕获并识别日语文字……";
            var result = await _workflow.RecognizeAndShowAsync(SelectedWindow);
            Status = result.Words.Count == 0
                ? "没有识别到日语词。"
                : $"已识别 {result.Words.Count} 个词，并绘制下划线。";
        }
        catch (Exception ex)
        {
            _workflow.Hide();
            Status = $"识别失败：{ex.Message}";
        }
    }

    /// <summary>隐藏下划线覆盖层。</summary>
    [RelayCommand]
    private void Hide()
    {
        _workflow.Hide();
        Status = "下划线已隐藏。";
    }
}
