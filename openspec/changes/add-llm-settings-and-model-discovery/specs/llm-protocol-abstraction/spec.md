# llm-protocol-abstraction 规范

## MODIFIED Requirements

### Requirement: Provide a pluggable provider-wire-protocol port
系统 SHALL 暴露一个协议端口：给定规范化的短语分析请求和模型名称，该端口生成供应商专属请求封装和请求路径，应用供应商专属认证请求头，并将供应商专属响应封装中的结构化分组数组提取为 JSON element。每个协议 SHALL 声明其原生结构化输出机制和认证策略，以约束供应商返回符合 schema 的 JSON。该端口 SHALL 独立实现 OpenAI Chat Completions、Anthropic Messages 和 OpenAI Responses 三种 wire 格式。

#### Scenario: 选择 OpenAI Chat Completions 协议
- **WHEN** 配置的 protocol 是 OpenAI Chat Completions
- **THEN** 请求发送到 `/chat/completions` 路径，使用 OpenAI `messages` 封装和严格的 `response_format` JSON schema，发送 Bearer 认证，并从 `choices[0].message.content` 读取分组数组

#### Scenario: 选择 Anthropic Messages 协议
- **WHEN** 配置的 protocol 是 Anthropic Messages
- **THEN** 请求发送到 `/v1/messages` 路径，使用包含 Anthropic `system`+`messages`+`max_tokens` 封装和强制 `tool_use`，发送 `x-api-key` 与 `anthropic-version: 2023-06-01` 而不发送 Bearer 认证，并从 `content[].tool_use.input` 读取分组数组

#### Scenario: 选择 OpenAI Responses 协议
- **WHEN** 配置的 protocol 是 OpenAI Responses
- **THEN** 请求发送到 `/responses` 路径，使用包含 OpenAI `input` 封装和严格 `text.format` JSON schema，发送 Bearer 认证，并从 `output` 数组的文本内容读取分组数组

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
