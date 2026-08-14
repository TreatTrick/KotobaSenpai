using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>
/// 诊断记录端口：把识别/分词结果落盘，供离线分析（如 AI 复盘 OCR 与分词质量）。
/// 实现按设置项开关；Core 只依赖端口，不直接写文件。
/// </summary>
public interface IDiagnosticReporter
{
    /// <summary>记录一次识别的分词结果（token 细节 + 包围盒）。</summary>
    void RecordTokens(WindowTarget target, IReadOnlyList<GroupedWord> groupedWords);

    /// <summary>记录一次 phrase 分析运行：句段/group 计数、提供方结果与校验警告。不记录截图、API key 或窗口标题。</summary>
    void RecordPhraseAnalysis(PhraseAnalysisOutcome outcome, IReadOnlyList<PhraseGroupView> groups, string? warning);
}