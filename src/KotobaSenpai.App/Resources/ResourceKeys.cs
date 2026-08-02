namespace KotobaSenpai.App.Resources;

/// <summary>
/// 所有 App 层用户可见文案的资源键常量（XAML 标签、ViewModel 状态文案）。
/// 与中性 <c>Strings.resx</c> 的键一一对应；值即资源键名。集中定义便于：
/// ViewModel 编译期拼写检查、架构测试枚举校验、以及"无缺失键"测试。
/// 错误码消息的键见 <c>KotobaSenpai.Core.Localization.ErrorCodes</c>。
/// </summary>
public static class ResourceKeys
{
    // --- XAML 标签 ---
    public const string MainWindow_Title = nameof(MainWindow_Title);
    public const string Label_TargetWindow = nameof(Label_TargetWindow);
    public const string Label_Actions = nameof(Label_Actions);
    public const string Label_Language = nameof(Label_Language);
    public const string Button_RefreshWindows = nameof(Button_RefreshWindows);
    public const string Button_Recognize = nameof(Button_Recognize);
    public const string Button_Hide = nameof(Button_Hide);
    public const string Label_Description = nameof(Label_Description);

    // --- ViewModel 状态文案（{0} 为占位符） ---
    public const string Status_SelectTarget = nameof(Status_SelectTarget);
    public const string Status_SelectTargetFirst = nameof(Status_SelectTargetFirst);
    public const string Status_Selected = nameof(Status_Selected);
    public const string Status_NoWindows = nameof(Status_NoWindows);
    public const string Status_WindowsFound = nameof(Status_WindowsFound);
    public const string Status_Recognizing = nameof(Status_Recognizing);
    public const string Status_NoWords = nameof(Status_NoWords);
    public const string Status_WordsRecognized = nameof(Status_WordsRecognized);
    public const string Status_Hidden = nameof(Status_Hidden);
}
