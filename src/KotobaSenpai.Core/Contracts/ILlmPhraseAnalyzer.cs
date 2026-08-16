using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>
/// Port: detects phrase groupings for a single sentence-level segment. Implementations live in the platform
/// layer (the first being a provider-agnostic adapter) and carry a cancellation token plus diagnostic/error
/// semantics. Core depends only on this port and is unaware of the specific provider.
/// </summary>
public interface ILlmPhraseAnalyzer
{
    Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default);
}