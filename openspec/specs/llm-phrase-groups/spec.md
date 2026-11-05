# llm-phrase-groups Specification

## Purpose
TBD - created by archiving change add-llm-phrase-groups. Update Purpose after archive.
## Requirements
### Requirement: 按当前语言分析有本地依据的句段

系统 SHALL 通过与提供方无关的分析接口按句段提交 OCR 原文、稳定词元引用、UniDic 元数据及本地连续 JMdict 词块摘要，并携带该次分析开始时读取的语言名称和整句翻译开关值。语义请求内容 SHALL 不包含截图、坐标、窗口标题或 API Key。相同语义内容 SHALL 可以按配置通过三种现有协议发送。调用模型前 SHALL 检查原文、语言及是否要求译文三项是否匹配现有缓存，只有三者都匹配时才复用。已开始的请求 SHALL 沿用开始时的开关值。

#### Scenario: 请求具有完整本地依据
- **WHEN** 句段具有 UniDic 词元和本地连续词块
- **THEN** 请求包含原文、唯一词元标识、表面形式、词典形、读音、词性、活用信息、本地词块摘要、语言名称和是否要求译文

#### Scenario: 不发送截图等无关数据
- **WHEN** 应用请求模型分析 OCR 结果
- **THEN** 语义请求不含图片字节、屏幕矩形、窗口标题或 API Key；认证继续按协议放在请求头中

#### Scenario: 三种协议均能传递相同语义
- **WHEN** 配置为 OpenAI Chat Completions、Anthropic Messages 或 OpenAI Responses
- **THEN** 同一份语义请求被包装为对应协议的请求格式，并使用同一份语言和开关值

#### Scenario: 相同句子和语言命中缓存
- **WHEN** 原文、有效语言和是否要求译文都匹配当前有效缓存
- **THEN** 系统直接复用缓存结果，不调用模型

#### Scenario: 文本或语言不匹配时重新分析
- **WHEN** 缓存不存在，或原文、有效语言、是否要求译文任一不匹配
- **THEN** 系统使用本次读取的语言和开关调用模型，并按本次请求的三项条件缓存成功结果

### Requirement: 只返回有解释价值的组合组

`groups[]` SHALL 包含零个或多个有教学价值的组合，不要求在组数组中重复本地已知的普通词或连续词块。每个组 SHALL 包含请求内组标识、组类型、一个或多个组成部分、原文标签，以及用请求有效语言书写的 `meaning` 和 `grammar`。词级解释 SHALL 继续由独立的 `words[]` 承载。

#### Scenario: 返回不连续组合
- **WHEN** 句段中存在被其他词元隔开的有意义表达
- **THEN** 结果包含带有多个部分的组、原文标签，以及目标语言的释义和语法说明

#### Scenario: 没有组合时保留本地结果
- **WHEN** 句段不存在值得解释的组合
- **THEN** 返回空的组数组，本地词元和词块结果继续可用

### Requirement: Reference existing contiguous token parts
Every group part SHALL be a non-empty, ordered list of token IDs from the same request segment. Token IDs within one part SHALL represent a contiguous token sequence. A group SHALL contain one or more parts, and a token SHALL NOT be repeated within the same group. Different groups MAY reference overlapping tokens.

#### Scenario: Accept a cross-line continuous part
- **WHEN** a meaningful word is split across two accepted adjacent OCR lines
- **THEN** the group contains one ordered part referencing the tokens across the line boundary and the application treats it as one group

#### Scenario: Accept separated parts
- **WHEN** a grammar pattern has two meaningful token sequences separated by intervening tokens
- **THEN** the group contains separate contiguous parts and does not include the intervening tokens in either part

#### Scenario: Reject an invalid reference
- **WHEN** a group references an unknown token ID, repeats a token within itself, or lists non-contiguous IDs inside a part
- **THEN** the application drops that group and continues processing other valid groups and local results

### Requirement: Bound and validate model output
The system SHALL accept at most eight valid groups per sentence segment. It SHALL validate JSON shape, required fields, field lengths, group ID uniqueness within the response, token ownership, part ordering, and segment ownership before rendering. Invalid groups SHALL NOT abort the whole local recognition result.

#### Scenario: Cap an oversized response
- **WHEN** the provider returns more than eight groups
- **THEN** the application keeps at most the first eight groups in provider order after validation and records a diagnostic warning

#### Scenario: Ignore malformed groups individually
- **WHEN** one group has malformed fields but another group is valid
- **THEN** the valid group is retained, the malformed group is discarded, and local words/spans remain available

### Requirement: Assign application-owned group identity
The provider SHALL return only a request-local model group ID. After validation, the application SHALL assign a unique session group ID and use that ID for all parts, geometry, hover state, and detail presentation.

#### Scenario: Repeated model IDs across requests
- **WHEN** two separate analysis requests both return model group ID `g1`
- **THEN** the application assigns different session group IDs and does not merge their highlights

### Requirement: Preserve local fallback
识别 SHALL 始终执行云端短语分析流程，分析器作为识别服务的必需依赖，不提供独立本地分析的禁用分支。等待云端响应时 SHALL 先显示本地读音，不提前显示成功分析的下划线。缺少配置、取消、超时、传输失败、服务拒绝或无效响应 SHALL 返回可重试状态，不使应用崩溃，也不将本地结果伪装成完整分析成功。只有成功句段 SHALL 获得分析下划线和服务商解释。历史启用开关 SHALL 不影响该流程。

#### Scenario: Provider timeout
- **WHEN** 云端请求超时
- **THEN** 保留本地读音且不渲染无效组合组，显示可重试失败状态，不进入独立本地模式

#### Scenario: Missing API key
- **WHEN** 没有配置 API Key
- **THEN** 分析流程返回需要配置的状态，保留读音预览而不显示分析成功的下划线，不发送缺少凭据的请求

#### Scenario: Historical disabled value
- **WHEN** 历史设置中的启用值为 false 或缺失
- **THEN** 仍进入相同云端分析流程，不保留绕过分析的本地分支

#### Scenario: Partial sentence failure
- **WHEN** 一个句段成功而另一个句段失败
- **THEN** 最终只对成功句段显示下划线，保留所有句段的读音，并报告可重试警告

### Requirement: Indicate group membership by member-word underline on hover
系统 SHALL NOT 为语法组合组绘制独立的上划线。组的成员关系 SHALL 通过高亮其成员本地合并词的下划线来呈现：当某个组被悬停/选中时，覆盖该组 parts 所引用 token 的本地合并词 SHALL 将其下划线渲染为高亮样式，并显示该组的详情弹窗；组未被悬停时，成员词 SHALL 仅显示普通下划线，不呈现任何组信号。

#### Scenario: Highlight member words on hover
- **WHEN** 一个语法组合组被悬停/选中
- **THEN** 覆盖该组 parts token 的本地合并词下划线被高亮，并显示该组详情弹窗

#### Scenario: No overline drawn
- **WHEN** 存在一个已验证的语法组合组
- **THEN** 任何时刻都不在其 token 上方绘制独立上划线

#### Scenario: Rest shows no group signal
- **WHEN** 没有组被悬停
- **THEN** 成员词仅显示各自普通的本地下划线，不呈现组信号

### Requirement: Segment by sentence-final punctuation and analyze sentences concurrently
系统 SHALL 将 OCR 行按句末标点(`。！？…‥．`)或大段落间距断开成句段;**不因阅读顺序不可靠而断开**。每个句段作为一个独立连续的文本块,本地分词与 LLM 请求使用同一句段边界。系统 SHALL 对每个句段并发发起一个 LLM 请求(复用既有的并发上限),每个请求 SHALL 仅携带该句段的 token 表。`」` 不单独作为断句依据(`。」`/`？」` 已含句末标点)。

#### Scenario: Merge a wrapped sentence across lines
- **WHEN** 一句长句被折行,行1 末尾无句末标点、行2 继续
- **THEN** 两行并为一个句段,一次分词、一次 LLM 请求,跨行词在段内完整

#### Scenario: Break only at sentence-final punctuation or a paragraph gap
- **WHEN** 一行以句号结尾,或两行间隔超过段落间距
- **THEN** 该处断开为两个句段,各自独立分词与请求

#### Scenario: An embedded closing quote does not break a sentence
- **WHEN** 一句内嵌引用的中途出现 `」`(如 `「彼は「行こう」と`),但非句末
- **THEN** 该行不因 `」` 单独断开,继续并入当前句段

#### Scenario: Each request carries only its segment's tokens
- **WHEN** 一个句段发起 LLM 请求
- **THEN** 该请求的 token 表只含本句段的 token,不含其他句段

### Requirement: 同一次分析返回整句译文

系统 SHALL 按本次请求的 `IncludeSentenceTranslation` 值决定是否要求译文。开启时，在 `groups` 和 `words` 之外要求模型返回 `translation` 字符串；关闭时只要求词义和语法，不要求整句译文，也不增加单独的翻译请求。开启时的译文 SHALL 自然、忠实，使用请求的释义语言，保留否定和条件等关系，不凭空添加人物姓名或剧情；无法翻译时要求空字符串。指令 SHALL 继续使用统一英文模板。

#### Scenario: 词义语法和译文一起返回
- **WHEN** 开关开启的句段请求完成正常分析
- **THEN** 同一次响应提供 `groups`、`words` 和 `translation`，不要求模型回传原文、句段编号或屏幕位置

#### Scenario: 目标语言用于整句翻译
- **WHEN** 请求使用“法语”作为释义语言
- **THEN** 固定指令要求整句译文和词义语法都使用法语，不按“法语”二字的书写文字选择中文

#### Scenario: 关闭时不生成译文
- **WHEN** 本次请求的整句翻译开关为关闭
- **THEN** 提示词只要求词义和语法，不声明 `translation`；供应商额外返回的译文也被忽略，正常分析继续

### Requirement: 单独检查译文字段

本次要求译文时，系统 SHALL 对译文去掉两端空白。字段缺失、不是字符串、去空白后为空或超过 4000 字符时，系统 SHALL 将它视为无译文，保留其他合法分析内容，不单独自动重试，不截断后当作完整译文。关闭的请求 SHALL 不检查或发布额外译文字段，也不因没有译文报错。整个响应无法解析成有效 JSON 时 SHALL 继续遵守现有失败处理。

#### Scenario: 译文字段类型错误
- **WHEN** `translation` 是数组或对象，但词义和语法数据合法
- **THEN** 丢弃译文，保留合法内容，并允许弹窗显示无译文提示

#### Scenario: 返回的译文过长
- **WHEN** 译文去掉两端空白后有 4001 个字符
- **THEN** 不展示该译文，不影响合法的词义和语法

#### Scenario: 接受长度边界内的译文
- **WHEN** 译文是非空字符串，去掉两端空白后不超过 4000 个字符
- **THEN** 保存完整的去空白后译文

### Requirement: 本地建立句段与译文的关联

系统 SHALL 在汇总分析时，把可用译文与本次请求的原文、语言和句段标识关联，再传给覆盖层。单词和短语的译文入口 SHALL 依赖这个关联。系统 SHALL 不信任模型生成的原文或位置来决定显示对象。

#### Scenario: 汇总多个句段
- **WHEN** 同一轮识别中的多个句段分别返回译文
- **THEN** 每份译文关联到各自的本地请求，不因模型返回顺序或重复词语而混用
