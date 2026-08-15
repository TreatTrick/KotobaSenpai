namespace KotobaSenpai.Core.Models;

/// <summary>
/// 编排器对一次识别的 phrase 分析运行结果：成功与否、已验证并分配会话 ID 的 group 视图（帧坐标），
/// 以及一个可重试的警告。失败或全无效时 Groups 为空，本地词/span 不受影响。
/// </summary>
public sealed record PhraseAnalysisRun(PhraseAnalysisOutcome Outcome, IReadOnlyList<PhraseGroupView> Groups, string? Warning = null)
{
    public bool Succeeded => Outcome == PhraseAnalysisOutcome.Success;
}

/// <summary>覆盖层可见的 phrase group 视图：共享会话 ID、展示字段、提供方顺序与逐 part 几何。</summary>
public sealed record PhraseGroupView(
    Guid SessionGroupId,
    string Label,
    string Type,
    string Meaning,
    string Grammar,
    int ProviderOrder,
    int DistinctTokenCount,
    IReadOnlyList<PhrasePartView> Parts);

/// <summary>一个 part 的可绘制几何：按行拆分为多个矩形，绝不跨越空白区域。</summary>
public sealed record PhrasePartView(IReadOnlyList<ScreenRect> Rects);