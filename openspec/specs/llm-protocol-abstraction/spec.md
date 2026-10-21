# llm-protocol-abstraction Specification

## Purpose
TBD - created by archiving change abstract-llm-protocol. Update Purpose after archive.
## Requirements
### Requirement: Provide a pluggable provider-wire-protocol port
The system SHALL expose a protocol port that, given a canonical phrase-analysis request and a model name, produces the provider-specific request envelope and fallback request paths, applies protocol-specific authentication headers, and extracts the structured group array from the provider-specific response envelope as a JSON element. Chat SHALL use JSON object mode with a prompt-level structure example, while Anthropic and Responses SHALL declare their native structured-output mechanisms; returned results SHALL continue through local parsing and semantic validation. The port SHALL be implemented independently for OpenAI Chat Completions, Anthropic Messages, and OpenAI Responses wire formats.

#### Scenario: Select the OpenAI Chat Completions protocol
- **WHEN** the configured protocol is OpenAI Chat Completions
- **THEN** the request uses an OpenAI `messages` envelope with `response_format.type=json_object`, includes a JSON structure example in the system message, sets `max_tokens=4096`, sends Bearer authentication, and reads the group array from `choices[0].message.content`; endpoint fallback uses `/v1/chat/completions` and `/chat/completions`

#### Scenario: Select the Anthropic Messages protocol
- **WHEN** the configured protocol is Anthropic Messages
- **THEN** the request uses an Anthropic `system`+`messages`+`max_tokens` envelope with a forced `tool_use`, sends `x-api-key` and `anthropic-version: 2023-06-01` without Bearer authentication, and reads the group array from `content[].tool_use.input`; endpoint fallback uses `/v1/messages` and `/anthropic/v1/messages`

#### Scenario: Select the OpenAI Responses protocol
- **WHEN** the configured protocol is OpenAI Responses
- **THEN** the request uses an OpenAI `input` envelope with a strict `text.format` JSON schema, sends Bearer authentication, and reads the group array from the `output` array's text content; endpoint fallback uses `/v1/responses` and `/responses`

### Requirement: Select the protocol by configuration
The system SHALL select the active wire protocol from the current BYOK settings key for each new phrase-analysis or model-discovery request, defaulting to OpenAI Chat Completions when unset or unknown. Changing the key SHALL change the protocol implementation and authentication strategy used by subsequent requests without changing the phrase-analysis port contract.

#### Scenario: Default to OpenAI Chat Completions
- **WHEN** no protocol is configured or the stored value is unknown
- **THEN** the next request uses the OpenAI Chat Completions protocol and Bearer authentication

#### Scenario: Switch protocol by configuration
- **WHEN** the protocol setting is changed to Anthropic Messages
- **THEN** subsequent phrase requests and model-list requests use the Anthropic Messages paths, envelopes, and authentication headers

#### Scenario: Change protocol without restarting
- **WHEN** the protocol setting changes while the application is running and before the next request starts
- **THEN** the next request resolves the new protocol without rebuilding the application or restarting the process

### Requirement: Preserve semantic content across protocols
All three protocols SHALL transmit the same canonical semantic content — the segment text, stable token references with UniDic metadata, and locally resolved continuous span summaries — and SHALL produce the same group array for validation. The protocol abstraction SHALL add no semantics beyond wire-format translation and authentication metadata handling.

#### Scenario: Same payload across protocols
- **WHEN** the same phrase request is sent through any of the three protocols
- **THEN** each protocol's structured group array is validated by the same group parser and yields the same groups

#### Scenario: Fall back when a provider returns non-JSON
- **WHEN** a provider ignores its structured-output declaration and returns text that is not a JSON array
- **THEN** the group parser records a malformed-JSON warning and local words/spans remain available

### Requirement: 语言感知的共享 prompt
在协议信封包装之前构建的共享语义 prompt SHALL 针对当前激活的 UI 文化本地化，且所选中的任一协议 SHALL 传输同一份本地化 prompt。结构化输出 schema SHALL 声明语言中性的 `meaning` 与 `grammar` 字段，而非旧的 `meaningZh`/`grammarZh` 字段名。

#### Scenario: 本地化 prompt 跨协议复用
- **WHEN** 当前 UI 文化为 `en` 且通过任一协议发送一个短语请求
- **THEN** 每个协议 SHALL 传输由共享 prompt 构建器构建的同一份英文 prompt。

#### Scenario: schema 使用语言中性字段名
- **WHEN** 任一协议构建其结构化输出声明
- **THEN** group schema SHALL 要求 `meaning` 与 `grammar` 字段，而非 `meaningZh`/`grammarZh`。

#### Scenario: 本地化后各协议产出一致的分组
- **WHEN** 同一份本地化的短语请求通过三种协议中任一发送
- **THEN** 每个协议的结构化 group 数组由同一个 group 解析器校验并得到相同的 groups。

### Requirement: 优先请求配置地址并有条件回退
短语分析 SHALL 首先原样 POST 配置的地址，包括完整接口路径及查询参数。仅在 HTTP 404/405 时 SHALL 尝试下一个有限候选地址。候选 SHALL 按协议生成、保留自定义前缀和查询参数、使用 URI 相等性去重，且不重复追加版本段或完整协议路径。

#### Scenario: 完整地址直接成功
- **WHEN** 用户填写完整接口地址且该地址返回可解析的 HTTP 成功响应
- **THEN** 分析器只向该地址发送一次请求，不追加任何协议路径

#### Scenario: 在路径不存在时继续
- **WHEN** 原地址或后续候选返回 HTTP 404 或 405
- **THEN** 分析器使用相同的请求体与协议认证尝试下一个唯一候选，成功后立即停止

#### Scenario: 全部地址不可用
- **WHEN** 所有候选均返回 HTTP 404/405
- **THEN** 分析器返回 Refused，警告包含最终 HTTP 状态码和已尝试地址数量

#### Scenario: 其他失败不触发地址切换
- **WHEN** 返回 HTTP 400、401、403、429 或 5xx，或发生超时、传输错误、取消或成功响应格式错误
- **THEN** 分析器立即返回对应失败结果，不再尝试其他地址

### Requirement: 保留 HTTP 尝试的错误诊断
启用诊断时，每次 HTTP 失败 SHALL 保存请求体、脱敏后的响应体及空的 groups/words 数组；日志 SHALL 记录协议、目标路径、状态码和耗时，不记录认证请求头或 URL 查询参数。

#### Scenario: 错误响应回显 API Key
- **WHEN** HTTP 错误响应中包含当前配置的 API Key
- **THEN** 诊断中该值被替换为 `[REDACTED]`，日志与结果警告不暴露该值

