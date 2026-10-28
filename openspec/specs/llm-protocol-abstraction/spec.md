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

协议信封包装前的分析指令 SHALL 使用英文模板及该请求使用的目标语言名称，不再按界面文化翻译。三种协议 SHALL 共用同样的分析语义和目标语言规则，同时保留各自的工具调用、JSON 对象或严格结构要求。结构化输出 SHALL 继续使用语言中性的 `meaning`、`grammar` 字段，不使用 `meaningZh`、`grammarZh`。协议选择、认证、地址回退、结果解析和校验 SHALL 保留原有行为。

#### Scenario: 任一协议都要求指定语言
- **WHEN** 同一分析请求通过 OpenAI Chat Completions、Anthropic Messages 或 OpenAI Responses 发送
- **THEN** 每种协议的英文指令都使用该请求的目标语言，保持相同的词级和短语级解释范围

#### Scenario: 结构字段不随释义语言变化
- **WHEN** 任一协议构建法语或韩语分析请求
- **THEN** 结构声明继续使用 `meaning`、`grammar` 等既有字段名，不翻译字段名，也不恢复旧的中文专用字段名

#### Scenario: 共用本地解析与校验
- **WHEN** 三种协议分别收到相同语义的结构化分析结果
- **THEN** 各协议提取后仍交给同一套词级、组级解析器及校验器处理

#### Scenario: 输出机制保持协议原有约定
- **WHEN** 用户修改释义语言后分别使用三种协议
- **THEN** Chat 继续使用 JSON 对象模式和示例，Anthropic 继续强制调用结果工具，Responses 继续使用严格结构声明，认证和接口路径规则不改变

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

### Requirement: 共用显式配置的供应商请求执行能力
正式识别、设置页检测和 CLI 检测 SHALL 使用同一套协议注册、认证、地址候选、请求构造、HTTP 执行、响应提取、提示构造、解析与语义校验实现。公共执行层 SHALL 接收不可变的显式配置快照，正式识别适配从设置中读取快照，验证适配传入候选快照。CLI SHALL 不包含另一套供应商协议或认证实现。Core SHALL 保持无 HTTP、WPF 和文件系统依赖，Platform.Windows SHALL 不反向依赖 App。

#### Scenario: 相同输入经过不同入口
- **WHEN** UI、CLI 或正式识别使用相同协议、地址、模型、密钥、目标释义语言和规范请求
- **THEN** 产生等价的认证、地址候选、请求体和响应解析行为，不因入口不同而改用另一套供应商格式

#### Scenario: 新增受支持协议
- **WHEN** 将来在共享注册表加入协议实现
- **THEN** 各入口从注册表获取能力，不需要分别修改 CLI 和 UI 的协议实现副本

#### Scenario: 保留正式识别回退规则
- **WHEN** 正式识别收到 HTTP 400、401、403、429、5xx 或解析失败
- **THEN** 保留既有失败与地址回退规则，不因自动检测能力而在正式识别中无界切换协议或地址

#### Scenario: 验证候选不改设置
- **WHEN** 对与当前设置不同的候选执行检测
- **THEN** 公共执行层使用显式快照完成请求，不通过临时写入 `ISettingsService` 模拟候选
