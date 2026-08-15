## Context

当前 `src/KotobaSenpai.Platform.Windows/Llm/` 下，`DeepSeekPhraseAnalyzer`（HTTP 传输 + 错误映射）直接持有 `PhraseRequestBuilder`（拼 OpenAI `messages` 请求体）与 `PhraseResponseParser`（解 OpenAI `choices[0].message.content` 信封 + 解析 group JSON）。协议细节与商务逻辑耦合。DI 装配在 `App.xaml.cs:107-110`。

目标：把「提供方线上协议」抽成可插拔端口，同一套语义内容（system prompt、token 表、用户内容、group JSON 解析）可走 OpenAI Chat Completions、Anthropic Messages、OpenAI Responses 三种信封；用 BYOK 配置选择，默认保持现在的 OpenAI Chat Completions 行为。

## Goals / Non-Goals

**Goals:**
- `ILlmProtocol` 端口：给定规范化的语义请求与模型名，产出协议路径、请求体信封、从响应信封提取助手文本。
- 三个端口实现：OpenAI Chat Completions（`/chat/completions`）、Anthropic Messages（`/v1/messages`）、OpenAI Responses（`/responses`）。
- 语义内容只构造一次，各协议只包信封；group JSON 解析仍共享一份。
- `DeepSeekPhraseAnalyzer` 降为协议无关的传输层。
- 新配置键 `DeepSeekProtocol` 选择协议，默认 OpenAI Chat Completions，现有行为零回归。

**Non-Goals:**
- 不改 `ILlmPhraseAnalyzer` 端口契约（Core 与 Orchestrator 不动）。
- 不做流式、多模态、图片输入。
- 不新增外部 HTTP 依赖（信封用 `System.Text.Json` 手写）。
- 不做多轮对话/状态化 agent 编排——结构化输出只用于「单轮取回 group JSON」这一种能力，不接通用 tool calling 循环（Anthropic 的 `tool_use` 仅作为结构化输出的载体，不执行工具）。

## Decisions

### D1. 端口形状：三个方法，只覆盖三处真实差异
```csharp
public interface ILlmProtocol
{
    string Path { get; }                       // POST 目标相对路径
    string BuildBody(string systemPrompt, string userContent, string model);
    JsonElement ExtractGroupsJson(string envelopeJson);   // 读各协议的结构化输出位置
}
```
三者的语义层完全一致（同一模型、同一 prompt、同一 group JSON payload），差异只在①路径、②请求信封结构（含各自的结构化输出声明）、③响应里结构化 group JSON 的位置。暴露最小端口，不做多余抽象。HTTP、Bearer 鉴权、错误映射、取消/超时全留在 `DeepSeekPhraseAnalyzer`。

端口返回 `JsonElement`（group 数组的根），**不是**「助手文本」字符串：Anthropic 的结构化输出是原生 JSON 对象（`tool_use.input`），OpenAI 是严格 JSON 字符串——统一到 `JsonElement` 才能让解析层只做字段校验，不做格式猜测。

**替代方案**: 把 print 整个语义请求也交给协议 → 拒绝，语义内容构造会三份重复，违反「只包信封」。把 `ILlmProtocol` 放进 Core → 拒绝，协议是平台层细节，Core 只依赖 `ILlmPhraseAnalyzer`。

### D2. 语义内容抽成共享 `PhrasePromptBuilder`
把现 `PhraseRequestBuilder` 拆成 `PhrasePromptBuilder`：产出 `(systemPrompt, userContent)` 字符串 + 保留 16KB 体积上限检查。token 表 / 本地 span 序列化只写一次，三个协议复用。`RequestTooLargeException` 语义不变。

### D2.1 每个协议启用原生结构化输出
请求信封在共享语义内容之外，各自声明结构化输出，让提供方**强制**返回 schema 合规的 JSON，而不是靠 prompt 碰运气：
- **OpenAI Chat Completions**: 顶层 `response_format: {type: "json_schema", json_schema: {name, schema, strict: true}}` → 响应 `choices[0].message.content` 是严格 JSON 字符串。
- **OpenAI Responses**: 顶层 `text.format: {type: "json_schema", name, schema, strict: true}` → 响应 `output[].content[].text` 是严格 JSON 字符串。
- **Anthropic Messages**: 强制唯一 `tool_use`（`tool_choice: {type: "tool", name: "return_groups"}` + `tools` 里定义 `return_groups` 的 `input_schema`）→ 响应 `content[].tool_use.input` 是原生 JSON 对象。

group schema 由协议层持有（`type`/`modelGroupId`/`parts`/`label`/`meaningZh`/`grammarZh` 的数组），与 `PhraseGroupValidator` 的校验字段保持一致。结构化输出是主路径；`ExtractJsonArray` 的模糊提取降级为「提供方不听话时的兜底」，不再作为主依赖。

### D3. 响应解析切两半
`PhraseResponseParser` 保留 `ParseGroups(JsonElement)`（严格校验 group 字段），协议相关的信封提取移入各协议 `ExtractGroupsJson`，返回 group 数组的 `JsonElement`，定位随协议：
- OpenAI Chat Completions: `choices[0].message.content`（JSON 字符串 → `JsonDocument.Parse`）
- OpenAI Responses: `output[].content[].text`（拼接 text → `JsonDocument.Parse`）
- Anthropic Messages: `content[].tool_use.input`（原生 JSON 对象，直接取 `JsonElement`）

现有 `PhraseResponseParser.Parse(string envelope)` 的 OpenAI 信封分支与 `ExtractJsonArray` 模糊提取删除——结构化输出保证内容就是 JSON 数组，解析层只做字段校验。若某提供方无视结构化输出返回了代码块/说明文字，`ParseGroups` 捕获后走原有 `MalformedJson` 警告路径，不影响本地结果。

### D4. 配置键与 DI 装配
`DeepSeekSettingsKeys` 新增 `DeepSeekProtocol`（枚举，settings 存字符串）。`App.xaml.cs` 用工厂按配置选协议：
```csharp
services.AddSingleton<ILlmProtocol>(sp => {
    var protocolKey = sp.GetRequiredService<ISettingsService>().GetValue(DeepSeekSettingsKeys.Protocol);
    return protocolKey switch {
        "anthropic"     => new AnthropicMessagesProtocol(),
        "responses"     => new OpenAiResponsesProtocol(),
        _               => new OpenAiChatCompletionsProtocol(),
    };
});
```
默认（未配置/未知值）落到 OpenAI Chat Completions，保证现有用户零配置改动。

### D5. 命名与成对回归测试
现 `DeepSeekPhraseAnalyzer` 保留类名（减少改动面），构造函数把 `PhraseRequestBuilder` 换成 `PhrasePromptBuilder + ILlmProtocol`。测试扩展：三个协议各自的 `BuildBody` 信封结构 + `ExtractAssistantText` 分支，以及 `PhrasePromptBuilder` 共享输出的体积上限回归。

## Risks / Trade-offs

- [提供方不完全支持 strict JSON schema / 结构化输出] → 结构化输出是主路径，但 `ParseGroups` 保留对「返回了非 JSON 文本」的兜底：捕获后走 `MalformedJson` 警告，本地结果不受影响；若某提供方实测不支持，单协议降级回模糊提取即可，其余协议不受牵连。
- [Anthropic 强制 tool_use 的 round-trip 复杂度] → 需预置 `return_groups` tool 定义 + `tool_choice` 强制；实现时按官方文档核对 `tool_use.input` 字段名，测试用固定样例锁定。
- [Anthropic / Responses 响应信封结构假设] → 手写解析前按官方文档核对 `content` / `output` 数组里的字段名；测试用固定样例锁定。
- [OpenAI Responses 的 `input` 与 Chat Completions 的 `messages` 语义不同] → 本场景只发单轮 system+user 文本，`input` 数组直接放两条 `{role, content}` 即可，无需多轮状态。
- [Anthropic 要求 `max_tokens`] → Anthropic 信封里补一个固定上限（沿用现有 16KB 体积上限的近似 token 数），tool_use 输出只需 group JSON，安全。
- [新增配置键暴露给用户] → 默认值保证向后兼容，未知值与未配置等同 OpenAI Chat Completions，不外抛。
- [模型本身对日语组合的「教学价值」判断是软性的] → 结构化输出只保证「格式」合规，不保证「内容」合格；语义质量仍由现有 `PhraseGroupValidator` 校验兜底，格式问题不引入新的内容判定。

## Migration Plan

1. 新增 `ILlmProtocol` 与三个实现（不改 `DeepSeekPhraseAnalyzer` 行为）。
2. `PhraseRequestBuilder` → `PhrasePromptBuilder` 重构，`DeepSeekPhraseAnalyzer` 改持 `ILlmProtocol`。
3. 新增 `DeepSeekProtocol` 配置键与 DI 工厂。
4. 跑全量测试回归（现有 OpenAI 路径用例必须绿）。
5. 手动切 `DeepSeekProtocol` 到 anthropic/responses，用火山 ARK 端点各发一次 phrase 请求验证信封。

## Open Questions

- Anthropic 端点 `.../coding` 的 `max_tokens` 上限取多少合适？先在实现时按 16KB 体积上限换算一个保守值，测试覆盖即可。