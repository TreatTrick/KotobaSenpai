# llm-protocol-abstraction 规范

## MODIFIED Requirements

### Requirement: Provide a pluggable provider-wire-protocol port
系统 SHALL 暴露一个协议端口：给定规范化的短语分析请求和模型名称，该端口生成协议专属请求封装和回退请求路径，应用协议专属认证请求头，并将响应封装中的结构化分组数组提取为 JSON element。Chat SHALL 使用 JSON object 模式与 prompt 结构示例，Anthropic 和 Responses SHALL 使用各自原生的结构化输出声明；返回结果 SHALL 继续通过本地解析和语义校验。该端口 SHALL 独立实现 OpenAI Chat Completions、Anthropic Messages 和 OpenAI Responses 三种 wire 格式。

#### Scenario: 选择 OpenAI Chat Completions 协议
- **WHEN** 配置的 protocol 是 OpenAI Chat Completions
- **THEN** 请求使用 OpenAI `messages` 封装和 `response_format.type=json_object`，在 system 消息中提供 JSON 结构示例，设置 `max_tokens=4096`，发送 Bearer 认证，并从 `choices[0].message.content` 读取分组数组；地址回退时使用 `/v1/chat/completions` 和 `/chat/completions`

#### Scenario: 选择 Anthropic Messages 协议
- **WHEN** 配置的 protocol 是 Anthropic Messages
- **THEN** 请求使用包含 Anthropic `system`+`messages`+`max_tokens` 封装和强制 `tool_use`，发送 `x-api-key` 与 `anthropic-version: 2023-06-01` 而不发送 Bearer 认证，并从 `content[].tool_use.input` 读取分组数组；地址回退时使用 `/v1/messages` 和 `/anthropic/v1/messages`

#### Scenario: 选择 OpenAI Responses 协议
- **WHEN** 配置的 protocol 是 OpenAI Responses
- **THEN** 请求使用包含 OpenAI `input` 封装和严格 `text.format` JSON schema，发送 Bearer 认证，并从 `output` 数组的文本内容读取分组数组；地址回退时使用 `/v1/responses` 和 `/responses`

### Requirement: Select the protocol by configuration
系统 SHALL 在每个新的短语分析或模型发现请求中，根据当前 BYOK 设置键选择活动 wire 协议；未设置或未知时默认使用 OpenAI Chat Completions。修改该键 SHALL 改变后续请求使用的协议实现和认证策略，同时不改变短语分析端口契约。

#### Scenario: 默认使用 OpenAI Chat Completions
- **WHEN** 未配置 protocol 或存储值未知
- **THEN** 下一次请求使用 OpenAI Chat Completions 协议和 Bearer 认证

#### Scenario: 通过配置切换 protocol
- **WHEN** protocol 设置改为 Anthropic Messages
- **THEN** 后续短语请求和模型列表请求使用 Anthropic Messages 的路径、封装和认证请求头

#### Scenario: 无需重启即可修改 protocol
- **WHEN** 应用运行期间且下一次请求开始前修改 protocol 设置
- **THEN** 下一次请求解析新的 protocol，无需重建应用或重启进程

### Requirement: Preserve semantic content across protocols
三个协议 SHALL 传输相同的规范化语义内容——片段文本、带 UniDic 元数据的稳定 token 引用以及本地解析的连续范围摘要——并 SHALL 产生相同的分组数组用于校验。协议抽象层 SHALL 只做wire 格式转换和认证元数据处理，不增加其他语义。

#### Scenario: 三种协议使用相同 payload
- **WHEN** 同一个短语请求通过三种协议中的任一协议发送
- **THEN** 每个协议产生的结构化分组数组都由相同的分组解析器校验，并得到相同的分组

#### Scenario: 供应商返回非 JSON 时回退
- **WHEN** 供应商忽略其结构化输出声明，返回不是 JSON 数组的文本
- **THEN** 分组解析器记录 malformed-JSON 警告，本地单词和范围仍然可用

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

## ADDED Requirements

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
