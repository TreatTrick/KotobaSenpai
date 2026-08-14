using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>
/// 端口：对单个句级 segment 做 phrase 组合检测。实现位于平台层（首个为 DeepSeek 兼容适配器），
/// 携带取消 token 与诊断/错误语义。Core 只依赖此端口，不感知具体提供方。
/// </summary>
public interface ILlmPhraseAnalyzer
{
    Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default);
}