# 三种协议共用释义语言规则

## MODIFIED Requirements

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
