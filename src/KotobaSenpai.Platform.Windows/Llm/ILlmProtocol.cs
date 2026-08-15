using System.Text.Json;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 提供方线上协议端口：只覆盖三种真实差异——POST 目标路径、请求体信封（含各自的结构化输出声明）、
/// 从响应信封里取出 group 数组的 <see cref="JsonElement"/>。HTTP、Bearer 鉴权、错误映射、取消/超时
/// 均留在传输层（<see cref="DeepSeekPhraseAnalyzer"/>），语义内容由 <see cref="PhrasePromptBuilder"/> 构造。
/// </summary>
public interface ILlmProtocol
{
    /// <summary>POST 目标相对路径（拼在配置的 endpoint 之后）。</summary>
    /// <example>OpenAI 为 <c>/chat/completions</c>，Anthropic 为 <c>/v1/messages</c>。</example>
    string Path { get; }

    /// <summary>把共享的语义 prompt 包成该协议的信封（含结构化输出声明），序列化为请求体。</summary>
    /// <example>
    /// OpenAI Chat Completions 产出的 body 形如：
    /// <code>
    /// { "model": "…", "temperature": 0.0,
    ///   "messages": [ { "role": "system", "content": systemPrompt },
    ///                 { "role": "user",   "content": userContent } ],
    ///   "response_format": { "type": "json_schema",
    ///     "json_schema": { "name": "return_groups", "schema": …, "strict": true } } }
    /// </code>
    /// Anthropic 改用 <c>tools[0].input_schema</c> + 强制 <c>tool_choice</c>，group 数组落在响应
    /// <c>content[].tool_use.input.groups</c>。
    /// </example>
    string BuildBody(string systemPrompt, string userContent, string model);

    /// <summary>从响应信封取出结构化 group 数组的根元素。结构不符抛 <see cref="PhraseResponseException"/>。</summary>
    /// <example>
    /// OpenAI Chat Completions 信封：<c>choices[0].message.content</c> 是内嵌 JSON 字符串，
    /// 其根下 <c>groups</c> 即返回目标。Anthropic 即便如此提取：
    /// <code>
    /// content[].tool_use.input.groups
    /// </code>
    /// </example>
    JsonElement ExtractGroupsJson(string envelopeJson);
}