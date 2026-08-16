using System.Text;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Builds the semantic prompt shared by the three protocols: system prompt + user content (segment text, token metadata,
/// and a summary of local consecutive spans). Only text and metadata are sent; screenshots, window coordinates, titles,
/// and API keys are never. Each <see cref="ILlmProtocol"/> owns the envelope (including the structured-output
/// declaration). Throws <see cref="RequestTooLargeException"/> when the size limit is exceeded. The prompt copy is
/// resolved by <see cref="IStringLocalizer"/> against the active culture; a runtime language switch takes effect on the
/// next request.
/// </summary>
public sealed class PhrasePromptBuilder
{
    public const int MaxBodyBytes = 16_000;

    private readonly IStringLocalizer _localizer;

    public PhrasePromptBuilder(IStringLocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>Returns (systemPrompt, userContent). Throws <see cref="RequestTooLargeException"/> when userContent exceeds the size limit.</summary>
    public (string SystemPrompt, string UserContent) Build(PhraseAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userContent = new StringBuilder(_localizer.Get("Llm.PhraseUserInstruction")).Append('\n');
        userContent.Append(_localizer.Get("Llm.SegmentLabel")).Append(request.SegmentText).Append('\n');
        userContent.Append(_localizer.Get("Llm.TokenTableLabel")).Append('\n');
        foreach (var token in request.Tokens)
            userContent.Append(token.Id).Append('|').Append(token.Token.Surface).Append('|')
                .Append(token.Token.Lemma).Append('|').Append(token.Token.Reading).Append('|')
                .Append(token.Token.PartsOfSpeech.Pos1).Append('|')
                .Append(token.Token.ConjugationType).Append('|').Append(token.Token.ConjugationForm).Append('\n');
        userContent.Append(_localizer.Get("Llm.LocalSpansLabel")).Append('\n');
        foreach (var span in request.LocalSpans)
            userContent.Append(span.Surface).Append('|').Append(span.Reading).Append('|')
                .Append(string.Join(",", span.TokenIds)).Append('\n');
        userContent.Append(_localizer.Get("Llm.WordsInstruction")).Append('\n');

        var content = userContent.ToString();
        if (Encoding.UTF8.GetByteCount(content) > MaxBodyBytes)
            throw new RequestTooLargeException(
                $"Phrase prompt content exceeds {MaxBodyBytes} bytes.");
        return (_localizer.Get("Llm.PhraseSystemPrompt"), content);
    }
}

/// <summary>The request prompt exceeds the provider's text limit.</summary>
public sealed class RequestTooLargeException(string message) : Exception(message);