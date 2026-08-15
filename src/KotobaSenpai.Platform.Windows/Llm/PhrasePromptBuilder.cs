using System.Text;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 构造三个协议共享的语义 prompt：system prompt + 用户内容（句段文本、token 元数据与本地连续 span 摘要）。
/// 只发文本与元数据，绝不发送截图、窗口坐标、标题或 API key。信封（含结构化输出声明）由各
/// <see cref="ILlmProtocol"/> 负责。超出体积上限时抛 <see cref="RequestTooLargeException"/>。
/// prompt 文案经 <see cref="IStringLocalizer"/> 按当前激活文化解析，运行时切语在下次请求时生效。
/// </summary>
public sealed class PhrasePromptBuilder
{
    public const int MaxBodyBytes = 16_000;

    private readonly IStringLocalizer _localizer;

    public PhrasePromptBuilder(IStringLocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>返回 (systemPrompt, userContent)。userContent 超过体积上限时抛 <see cref="RequestTooLargeException"/>。</summary>
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

        var content = userContent.ToString();
        if (Encoding.UTF8.GetByteCount(content) > MaxBodyBytes)
            throw new RequestTooLargeException(
                $"Phrase prompt content exceeds {MaxBodyBytes} bytes.");
        return (_localizer.Get("Llm.PhraseSystemPrompt"), content);
    }
}

/// <summary>请求 prompt 超出提供方文本上限。</summary>
public sealed class RequestTooLargeException(string message) : Exception(message);