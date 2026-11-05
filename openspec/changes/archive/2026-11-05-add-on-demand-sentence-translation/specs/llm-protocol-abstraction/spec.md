## ADDED Requirements

### Requirement: 三种协议使用相同的译文字段

OpenAI Chat Completions、Anthropic Messages 和 OpenAI Responses SHALL 按本次请求的开关决定输出结构。开启时，Chat 的 JSON 示例、Anthropic 的工具输入结构、Responses 的严格结构声明 SHALL 包含相同的 `translation` 字符串字段；关闭时 SHALL 只声明原有 `groups` 和 `words`，不要求译文。三种协议提取后 SHALL 使用同一套规则，不新增另一套翻译协议、认证或地址回退流程。

#### Scenario: Chat 返回译文
- **WHEN** 开关开启，使用 OpenAI Chat Completions 分析句段
- **THEN** JSON 对象模式和示例包含 `translation`，从现有消息内容中的分析对象取得译文

#### Scenario: Anthropic 返回译文
- **WHEN** 开关开启，使用 Anthropic Messages 分析句段
- **THEN** 现有强制结果工具要求字符串字段 `translation`，从该工具的输入对象取得译文

#### Scenario: Responses 返回译文
- **WHEN** 开关开启，使用 OpenAI Responses 分析句段
- **THEN** 严格结构声明要求字符串字段 `translation`，从现有输出文字中的分析对象取得译文

#### Scenario: 旧格式缺少译文
- **WHEN** 请求要求译文，但供应商返回合法旧格式，只有 `groups` 和 `words`
- **THEN** 三种协议均把整句译文视为缺失，其他合法结果仍交给现有流程处理

#### Scenario: 三种协议关闭翻译
- **WHEN** 相同请求在关闭整句翻译时分别使用三种协议
- **THEN** 示例或结构声明都不包含 `translation`，只生成词义和语法；额外返回译文也被忽略

#### Scenario: 连接检测复用新结构
- **WHEN** 设置页或命令行使用共享分析流程检测连接
- **THEN** 请求使用同样的开关规则；关闭时不要求译文，开启时仅因译文缺失也不单独判定连接失败，其他规则保持原有约定
