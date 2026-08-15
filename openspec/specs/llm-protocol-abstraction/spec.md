# llm-protocol-abstraction Specification

## Purpose
TBD - created by archiving change abstract-llm-protocol. Update Purpose after archive.
## Requirements
### Requirement: Provide a pluggable provider-wire-protocol port
The system SHALL expose a protocol port that, given a canonical phrase-analysis request and a model name, produces the provider-specific request envelope, the provider-specific request path, and extracts the structured group array from the provider-specific response envelope as a JSON element. Each protocol SHALL declare its native structured-output mechanism so the provider is constrained to return schema-compliant JSON. The port SHALL be implemented independently for OpenAI Chat Completions, Anthropic Messages, and OpenAI Responses wire formats.

#### Scenario: Select the OpenAI Chat Completions protocol
- **WHEN** the configured protocol is OpenAI Chat Completions
- **THEN** the request posts to the `/chat/completions` path with an OpenAI `messages` envelope plus a strict `response_format` JSON schema, and the group array is read from `choices[0].message.content`

#### Scenario: Select the Anthropic Messages protocol
- **WHEN** the configured protocol is Anthropic Messages
- **THEN** the request posts to the `/v1/messages` path with an Anthropic `system`+`messages`+`max_tokens` envelope plus a forced `tool_use`, and the group array is read from `content[].tool_use.input`

#### Scenario: Select the OpenAI Responses protocol
- **WHEN** the configured protocol is OpenAI Responses
- **THEN** the request posts to the `/responses` path with an OpenAI `input` envelope plus a strict `text.format` JSON schema, and the group array is read from the `output` array's text content

### Requirement: Select the protocol by configuration
The system SHALL select the active wire protocol from a BYOK settings key, defaulting to OpenAI Chat Completions when unset. Changing the key SHALL change which protocol implementation the provider analyzer uses without changing the phrase-analysis port contract.

#### Scenario: Default to OpenAI Chat Completions
- **WHEN** no protocol is configured
- **THEN** the analyzer uses the OpenAI Chat Completions protocol

#### Scenario: Switch protocol by configuration
- **WHEN** the protocol setting is changed to Anthropic Messages
- **THEN** subsequent phrase requests use the Anthropic Messages path and envelope

### Requirement: Preserve semantic content across protocols
All three protocols SHALL transmit the same canonical semantic content — the segment text, stable token references with UniDic metadata, and locally resolved continuous span summaries — and SHALL produce the same group array for validation. The protocol abstraction SHALL add no semantics beyond wire-format translation.

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

