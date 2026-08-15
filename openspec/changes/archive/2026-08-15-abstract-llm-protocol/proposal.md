## Why

当前 `DeepSeekPhraseAnalyzer` 把 OpenAI Chat Completions 协议（路径 `/chat/completions`、请求 `messages` 结构、响应 `choices[0].message.content` 信封）硬编码进商务逻辑。用户想用同一套 phrase 分析逻辑对接不同提供方协议（火山 ARK 的 Anthropic 兼容端点 `/coding`、OpenAI 兼容端点 `/coding/v3`、以及 OpenAI Responses API），逐个改 `DeepSeekPhraseAnalyzer` 会重复且不可维护。

## What Changes

- **BREAKING** 将「提供方线上协议」从 `DeepSeekPhraseAnalyzer` 中抽成独立接口 `ILlmProtocol`，三个实现分别封装 Anthropic Messages、OpenAI Chat Completions、OpenAI Responses 的路径、请求体信封、响应信封提取。
- `DeepSeekPhraseAnalyzer` 改为协议无关：只负责 HTTP 传输、Bearer 鉴权、错误映射、取消/超时，把「拼请求体」「提取助手文本」委托给所选协议。
- 新增 BYOK 配置键 `DeepSeekProtocol`（枚举：`OpenAiChatCompletions`（默认，保持现状）/ `AnthropicMessages` / `OpenAiResponses`），DI 据此挂载对应协议实现。
- 共享的语义内容构造（system prompt、token 表、用户内容、group JSON 解析）保持一处，各协议只包各自的信封。
- 现有 OpenAI Chat Completions 行为不变（默认协议），回归零风险。

## Capabilities

### New Capabilities
- `llm-protocol-abstraction`: 提供方线上协议的可插拔抽象——统一的协议端口、三种协议实现（Anthropic Messages / OpenAI Chat Completions / OpenAI Responses）、以及 BYOK 配置选择器。

### Modified Capabilities
- `llm-phrase-groups`: 端口 `ILlmPhraseAnalyzer` 的语义契约不变，但提供方实现从「单一 OpenAI 兼容适配器」扩展为「协议无关传输 + 可插拔协议」；新增协议选择配置与三种协议实现。

## Impact

- 代码：`src/KotobaSenpai.Platform.Windows/Llm/` 重构——拆出 `ILlmProtocol` 与三个实现，`DeepSeekPhraseAnalyzer` / `PhraseRequestBuilder` / `PhraseResponseParser` 拆分复用。
- 配置：新增 `DeepSeekProtocol` 枚举键与默认值。
- DI 注册：`PhraseAnalysisOrchestrator` 的 `ILlmPhraseAnalyzer` 装配处按配置选协议。
- 测试：`tests/KotobaSenpai.Platform.Windows.Tests/LlmPhraseAnalyzerTests.cs` 扩展三协议请求体/信封解析用例。
- 无新增外部依赖（三个协议均手写 JSON 信封，或复用已在用的 `System.Text.Json`）。