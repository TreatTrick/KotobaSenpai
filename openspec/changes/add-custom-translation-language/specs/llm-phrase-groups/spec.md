# 短语组合使用目标语言

## RENAMED Requirements

- FROM: `### Requirement: Analyze grounded sentence segments with an LLM`
- TO: `### Requirement: 按当前语言分析有本地依据的句段`
- FROM: `### Requirement: Return only combination groups`
- TO: `### Requirement: 只返回有解释价值的组合组`

## MODIFIED Requirements

### Requirement: 按当前语言分析有本地依据的句段

系统 SHALL 通过与提供方无关的分析接口按句段提交 OCR 原文、稳定词元引用、UniDic 元数据及本地连续 JMdict 词块摘要，并携带该次分析开始时读取的当前语言名称。语义请求内容 SHALL 不包含截图、坐标、窗口标题或 API Key。相同语义内容 SHALL 可以按配置通过三种现有协议发送。调用模型前 SHALL 检查句子原文和语言名称是否匹配现有缓存；只有两者都匹配时才直接复用。

#### Scenario: 请求具有完整本地依据
- **WHEN** 句段具有 UniDic 词元和本地连续词块
- **THEN** 请求包含原文、唯一词元标识、表面形式、词典形、读音、词性、活用信息、本地词块摘要和语言名称

#### Scenario: 不发送截图等无关数据
- **WHEN** 应用请求模型分析 OCR 结果
- **THEN** 语义请求不含图片字节、屏幕矩形、窗口标题或 API Key；认证继续按协议放在请求头中

#### Scenario: 三种协议均能传递相同语义
- **WHEN** 配置为 OpenAI Chat Completions、Anthropic Messages 或 OpenAI Responses
- **THEN** 同一份语义请求被包装为对应协议的请求格式，并发送到对应接口

#### Scenario: 相同句子和语言命中缓存
- **WHEN** 句子原文与有效语言都匹配当前有效缓存
- **THEN** 系统直接复用缓存结果，不调用模型

#### Scenario: 文本或语言不匹配时重新分析
- **WHEN** 缓存不存在，或句子原文、有效语言任一不匹配
- **THEN** 系统按该次分析读取的语言调用模型，并按该请求使用的语言缓存成功结果

### Requirement: 只返回有解释价值的组合组

`groups[]` SHALL 包含零个或多个有教学价值的组合，不要求在组数组中重复本地已知的普通词或连续词块。每个组 SHALL 包含请求内组标识、组类型、一个或多个组成部分、原文标签，以及用请求有效语言书写的 `meaning` 和 `grammar`。词级解释 SHALL 继续由独立的 `words[]` 承载。

#### Scenario: 返回不连续组合
- **WHEN** 句段中存在被其他词元隔开的有意义表达
- **THEN** 结果包含带有多个部分的组、原文标签，以及目标语言的释义和语法说明

#### Scenario: 没有组合时保留本地结果
- **WHEN** 句段不存在值得解释的组合
- **THEN** 返回空的组数组，本地词元和词块结果继续可用
