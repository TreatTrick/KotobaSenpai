# i18n-llm-prompts Specification

## Purpose
描述 LLM 短语分析请求的 prompt 及其返回值如何适配当前激活的 UI 文化。

## ADDED Requirements

### Requirement: 本地化的请求 prompt
LLM 短语分析请求 SHALL 通过 Core 的 `IStringLocalizer` 端口按当前激活的 UI 文化解析其 system prompt、用户指令与用户内容标签，而非使用硬编码文本。`zh-CN` 的 prompt 文本 SHALL 为简体中文，`en` 的 SHALL 为英文。

#### Scenario: 激活中文文化时生成中文 prompt
- **WHEN** 当前 UI 文化为 `zh-CN` 且构建一个短语分析请求
- **THEN** system prompt、用户指令与用户内容标签 MUST 为简体中文资源值。

#### Scenario: 激活英文文化时生成英文 prompt
- **WHEN** 当前 UI 文化为 `en` 且构建一个短语分析请求
- **THEN** system prompt、用户指令与用户内容标签 MUST 为英文资源值。

#### Scenario: 缺失翻译时回退到英文
- **WHEN** 某个 prompt 资源键在当前激活文化下无对应值，但存在英文（中性）值
- **THEN** prompt MUST 使用该英文值，与中性文化回退规则一致。

### Requirement: 本地化的响应
system prompt SHALL 指示 LLM 以当前激活的 UI 语言书写每个 group 的 meaning 与 grammar 释义字段。`zh-CN` 的 prompt SHALL 指示简体中文，`en` 的 prompt SHALL 指示英文。

#### Scenario: 中文 prompt 产出中文释义
- **WHEN** 当前 UI 文化为 `zh-CN`
- **THEN** system prompt 指示 LLM 以简体中文书写 meaning 与 grammar 字段。

#### Scenario: 英文 prompt 产出英文释义
- **WHEN** 当前 UI 文化为 `en`
- **THEN** system prompt 指示 LLM 以英文书写 meaning 与 grammar 字段。

### Requirement: 语言中性的 group 字段
短语 group 的结构化输出 SHALL 对散文释义字段使用语言中性的字段名 `meaning` 与 `grammar`，与 LLM 被指示书写的语言无关。旧的 `meaningZh`/`grammarZh` 字段名 SHALL 不再被使用。

#### Scenario: schema 声明中性字段名
- **WHEN** 构建协议的结构化输出 schema
- **THEN** group 对象 SHALL 声明 `meaning` 与 `grammar` 字符串字段，而非 `meaningZh`/`grammarZh`。

#### Scenario: 解析器读取中性字段名
- **WHEN** 解析一条提供方响应
- **THEN** 解析器 SHALL 从每个 group 中读取 `meaning` 与 `grammar` 字段。