using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.TestSupport;

/// <summary>Cloud-success fixture for tests focused on recognition, geometry or UI behavior.</summary>
internal static class PhraseAnalysisStub
{
    public static PhraseAnalysisOrchestrator Create()
        => new(new Analyzer(), tokenBuilder: new SentenceTokenBuilder(new CharacterTokenizer()));

    private sealed class Analyzer : ILlmPhraseAnalyzer
    {
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
    }

    private sealed class CharacterTokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text)
            => (text ?? "").Select((character, index) => new Token(
                character.ToString(), character.ToString(), character.ToString(), character.ToString(), character.ToString(), character.ToString(),
                new PartsOfSpeech("noun", "", "", ""), "", "", "", index)).ToArray();
    }
}
