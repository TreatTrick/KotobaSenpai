using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 把 <see cref="PhraseAnalysisRequest"/> 序列化为 DeepSeek 兼容 chat-completions 请求体。
/// 只发送句段文本、token 元数据与本地连续 span 摘要；绝不发送截图、窗口坐标、标题或 API key。
/// 超出提供方文本上限时抛出 <see cref="RequestTooLargeException"/>。
/// </summary>
public sealed class PhraseRequestBuilder
{
    public const int MaxBodyBytes = 16_000;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private const string SystemPrompt =
        "你是日语视觉小说文本的短语组合分析器。只返回真正有教学价值的多词组合（非连续语法、搭配、"
        + "依赖上下文的惯用表达），不要重复已经由本地识别出的普通词或连续词块。"
        + "每个 group 的 parts 必须是 token id 的有序连续列表，part 之间可被其他 token 间隔。"
        + "只输出 JSON 数组，不要输出任何解释或 markdown。";

    public string BuildBody(PhraseAnalysisRequest request, string model)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokenTable = request.Tokens
            .Select(t => new
            {
                id = t.Id.ToString(),
                surface = t.Token.Surface,
                lemma = t.Token.Lemma,
                reading = t.Token.Reading,
                pos1 = t.Token.PartsOfSpeech.Pos1,
                cType = t.Token.ConjugationType,
                cForm = t.Token.ConjugationForm,
            });
        var spanList = request.LocalSpans
            .Select(s => new { surface = s.Surface, reading = s.Reading, tokenIds = s.TokenIds });

        var userContent = new StringBuilder();
        userContent.Append("句段文本：").Append(request.SegmentText).Append('\n');
        userContent.Append("token 表（id/surface/lemma/reading/pos1/cType/cForm）：\n");
        foreach (var token in tokenTable)
            userContent.Append(token.id).Append('|').Append(token.surface).Append('|').Append(token.lemma)
                .Append('|').Append(token.reading).Append('|').Append(token.pos1).Append('|')
                .Append(token.cType).Append('|').Append(token.cForm).Append('\n');
        userContent.Append("本地已识别连续词块：\n");
        foreach (var span in spanList)
            userContent.Append(span.surface).Append('|').Append(span.reading).Append('|')
                .Append(string.Join(",", span.tokenIds)).Append('\n');
        userContent.Append("请只返回组合 group 的 JSON 数组。");

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userContent.ToString() },
            },
            temperature = 0.0,
        };

        var body = JsonSerializer.Serialize(payload, Options);
        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
            throw new RequestTooLargeException(
                $"Phrase request body exceeds {MaxBodyBytes} bytes.");
        return body;
    }
}

/// <summary>请求体超出提供方文本上限。</summary>
public sealed class RequestTooLargeException(string message) : Exception(message);