using System.Text;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 构造三个协议共享的语义 prompt：system prompt + 用户内容（句段文本、token 元数据与本地连续 span 摘要）。
/// 只发文本与元数据，绝不发送截图、窗口坐标、标题或 API key。信封（含结构化输出声明）由各
/// <see cref="ILlmProtocol"/> 负责。超出体积上限时抛 <see cref="RequestTooLargeException"/>。
/// </summary>
public sealed class PhrasePromptBuilder
{
    public const int MaxBodyBytes = 16_000;

    private const string SystemPrompt =
        "你是日语视觉小说文本的短语组合分析器。只返回真正有教学价值的多词组合（非连续语法、搭配、"
        + "依赖上下文的惯用表达），不要重复已经由本地识别出的普通词或连续词块。"
        + "每个 group 的 parts 必须是 token id 的有序连续列表，part 之间可被其他 token 间隔。";

    private const string UserInstruction =
        "请只返回组合 group（遵循结构化输出的 schema）。";

    /// <summary>返回 (systemPrompt, userContent)。userContent 超过体积上限时抛 <see cref="RequestTooLargeException"/>。</summary>
    public (string SystemPrompt, string UserContent) Build(PhraseAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userContent = new StringBuilder(UserInstruction).Append('\n');
        userContent.Append("句段文本：").Append(request.SegmentText).Append('\n');
        userContent.Append("token 表（id/surface/lemma/reading/pos1/cType/cForm）：\n");
        foreach (var token in request.Tokens)
            userContent.Append(token.Id).Append('|').Append(token.Token.Surface).Append('|')
                .Append(token.Token.Lemma).Append('|').Append(token.Token.Reading).Append('|')
                .Append(token.Token.PartsOfSpeech.Pos1).Append('|')
                .Append(token.Token.ConjugationType).Append('|').Append(token.Token.ConjugationForm).Append('\n');
        userContent.Append("本地已识别连续词块：\n");
        foreach (var span in request.LocalSpans)
            userContent.Append(span.Surface).Append('|').Append(span.Reading).Append('|')
                .Append(string.Join(",", span.TokenIds)).Append('\n');

        var content = userContent.ToString();
        if (Encoding.UTF8.GetByteCount(content) > MaxBodyBytes)
            throw new RequestTooLargeException(
                $"Phrase prompt content exceeds {MaxBodyBytes} bytes.");
        return (SystemPrompt, content);
    }
}

/// <summary>请求 prompt 超出提供方文本上限。</summary>
public sealed class RequestTooLargeException(string message) : Exception(message);