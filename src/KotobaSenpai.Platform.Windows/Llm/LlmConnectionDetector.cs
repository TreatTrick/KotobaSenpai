using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Tries existing protocol/address candidates using the production analyzer and semantic validators.</summary>
public sealed class LlmConnectionDetector(LlmPhraseAnalyzer analyzer, LlmProtocolRegistry registry,
    SentenceTranslationPreferences? sentenceTranslation = null) : ILlmConnectionDetector
{
    public async Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var request = CreateSample(sentenceTranslation?.Enabled ?? true);
        var result = await analyzer.AnalyzeAsync(request, configuration, cancellationToken).ConfigureAwait(false);
        return Validate(request, configuration, result);
    }

    public async Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default)
    {
        var request = CreateSample(sentenceTranslation?.Enabled ?? true);
        var last = new LlmDetectionResult(null, AgentResultCode.UnsupportedProtocol);
        foreach (var protocolId in AgentApiContract.ProtocolOrder)
        {
            var protocol = registry.Resolve(protocolId);
            var endpoint = LlmEndpointHelper.ForProtocol(new Uri(candidate.Endpoint), protocol.Path);
            foreach (var address in LlmEndpointHelper.AnalysisCandidates(endpoint, protocol.Path))
            {
                if (cancellationToken.IsCancellationRequested)
                    return new(null, AgentResultCode.Cancelled);
                var configuration = candidate with { Protocol = protocolId, Endpoint = address.AbsoluteUri };
                var result = await analyzer.AnalyzeAsync(request, configuration, cancellationToken, singleEndpoint: true).ConfigureAwait(false);
                last = Validate(request, configuration, result);
                if (last.Succeeded || last.Code is AgentResultCode.Cancelled or AgentResultCode.Timeout or AgentResultCode.TransportError or AgentResultCode.RateLimited or AgentResultCode.ProviderUnavailable or AgentResultCode.RedirectRejected)
                    return last;
            }
        }
        return last;
    }

    private static LlmDetectionResult Validate(PhraseAnalysisRequest request, LlmConfiguration configuration, PhraseAnalysisResult result)
    {
        if (result.Succeeded)
        {
            var words = WordMeaningValidator.ValidateAndBuild(result.Words, request.LocalSpans);
            var groups = PhraseGroupValidator.ValidateAndBuild(result.Groups, request.Tokens.ToDictionary(token => token.Id));
            return words.ValidWords.Count > 0 || groups.ValidGroups.Count > 0 ? new(configuration)
                : new(null, AgentResultCode.InvalidAnalysis, "The response contained no valid word meanings or phrase groups.");
        }
        var code = result.Outcome switch
        {
            PhraseAnalysisOutcome.Cancelled => AgentResultCode.Cancelled,
            PhraseAnalysisOutcome.Timeout => AgentResultCode.Timeout,
            PhraseAnalysisOutcome.TransportError => AgentResultCode.TransportError,
            PhraseAnalysisOutcome.MalformedJson or PhraseAnalysisOutcome.InvalidResponse => AgentResultCode.InvalidAnalysis,
            _ => result.ProviderStatusCode switch
            {
                401 or 403 => AgentResultCode.AuthenticationFailed,
                429 => AgentResultCode.RateLimited,
                >= 500 => AgentResultCode.ProviderUnavailable,
                >= 300 and < 400 => AgentResultCode.RedirectRejected,
                _ => AgentResultCode.IncompatibleProtocol,
            },
        };
        return new(null, code, $"{configuration.Protocol}: {configuration.Endpoint}. {result.Warning ?? result.Outcome.ToString()}");
    }

    public static PhraseAnalysisRequest CreateSample(bool includeSentenceTranslation = true)
    {
        var token = new Token("日本語", "日本語", "日本語", "にほんご", "にほんご", "ニホンゴ",
            new PartsOfSpeech("名詞", "普通名詞", "一般", ""), "", "", "", 0);
        var reference = new SentenceTokenReference(0, 0, 0, 0, token, []);
        return new(Guid.NewGuid(), "connection-test", "日本語", [reference],
            [new LocalSpanSummary(token.Surface, token.Reading, token.Lemma, [reference.Id])], includeSentenceTranslation: includeSentenceTranslation);
    }
}
