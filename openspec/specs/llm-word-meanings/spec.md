# llm-word-meanings Specification

## Purpose
TBD - created by archiving change keep-jmdic-merge-add-llm-meanings. Update Purpose after archive.
## Requirements
### Requirement: 为每个本地词返回目标语言释义

系统 SHALL 在现有句段分析调用中，除 `groups[]` 外返回平行的 `words[]`，覆盖请求内 `LocalSpans` 的每个本地合并词，包括助词和助动词。每项 SHALL 用 `headword` 逐字复制对应词块的原文，并按请求的有效语言生成该词在语境中的单一最佳 `meaning` 和简洁 `grammar`。`pos` SHALL 保留现有日语词性约定。词项 SHALL 不返回词元标识或读音，不引用请求之外的实体；这些本地信息继续由词典和 UniDic 提供。

#### Scenario: 覆盖全部本地词块
- **WHEN** 句段包含学校、は、を等本地词块，包括助词或助动词
- **THEN** 每个词块各有一项词级记录，不挑选、不遗漏，解释字段统一要求当前目标语言

#### Scenario: 合并词按整体原文引用
- **WHEN** 本地把多个词元合并为 `でも` 等词块
- **THEN** 词项以合并后的原文作为 `headword`，不拆成多项，也不重复本地读音和词元信息

### Requirement: Bound and validate word output
系统 SHALL 在接受前校验每个词：headword 精确匹配请求内一个本地合并词的 surface、同一合并词不得被重复匹配、释义/语法/词性的字段长度受限。无效或匹配不到本地合并词的词 SHALL 被单独丢弃，不影响其余有效词、语法组或本地结果。每句段词级输出 SHALL 有宽松的上限，超出时保留前 N 个并记录诊断。

#### Scenario: Drop a word with an unmatched headword
- **WHEN** 一个词的 headword 匹配不到任何本地合并词（模型多算/错位/抄写误差）
- **THEN** 该词被丢弃，其他有效词继续可用，不发生级联错位

#### Scenario: Drop a duplicate headword
- **WHEN** 两个词引用了同一个本地合并词（headword 相同）
- **THEN** 仅保留第一个（按响应顺序），后一个被丢弃

#### Scenario: Cap an oversized response
- **WHEN** provider 返回的词条数超过每句段上限
- **THEN** 系统在验证后保留前 N 个词条并记录诊断警告

#### Scenario: Ignore malformed words individually
- **WHEN** 一个词缺字段或字段类型错误，而另一个词合法
- **THEN** 合法词被保留，畸形词被丢弃，本地词块/spans 保持可用

### Requirement: 悬停弹窗显示目标语言词义

系统 SHALL 在悬停本地合并词时，展示词头、词性、读音、音调及可用的目标语言释义和语法说明。读音和音调 SHALL 继续来自本地 UniDic；音调使用现有统一记法，例如 `[2] LH↓L`。释义 SHALL 仅在对应句段完整 AI 分析成功并完成最终覆盖层刷新后可用。没有有效释义时 SHALL 展示本地词头、读音、已知音调和按界面语言显示的“无释义”提示；未知音调不得伪造。弹窗位置 SHALL 在词的包围盒下方且不遮挡词，并继续遵守 `phrase-popup-presentation` 的鼠标停留、10 DIP 离开距离及窗口内输入不透传规则。

#### Scenario: 成功后显示目标语言解释
- **WHEN** 法语分析成功并发布后，光标悬停在有词级记录的本地词上
- **THEN** 弹窗展示法语释义和语法说明，以及原有词头、词性、读音和音调；界面标题仍按界面语言显示

#### Scenario: 合并词完整展示读音和音调
- **WHEN** 悬停由多个 UniDic 词元合成且有释义的词
- **THEN** 弹窗按源词元顺序展示合并读音和各个音调片段，不只显示第一个词元

#### Scenario: 没有词义时使用本地信息
- **WHEN** 悬停词没有可用的模型词项
- **THEN** 弹窗显示词头、读音、已知音调和界面语言的“无释义”提示，不为空白或崩溃

#### Scenario: 等待模型时不提前显示释义
- **WHEN** 对应句段仍在等待模型
- **THEN** 本地振假名和音调继续可用，弹窗只使用本地信息回退

#### Scenario: 失败句段不显示模型释义
- **WHEN** 对应句段的模型分析失败
- **THEN** 弹窗保留本地词头、读音和已知音调，不显示该句段的模型释义

#### Scenario: 切换悬停目标不残留旧词
- **WHEN** 光标离开悬停词超过现有延迟，或切换到另一个词
- **THEN** 弹窗按原有规则隐藏或更新，不残留上一词的内容

### Requirement: Show per-word meanings within a phrase group
系统 SHALL 在选中/悬停一个语法组合组（`ParsedPhraseGroup`）并展示其详情时，除组级 label、释义与语法外，还逐词列出组内每个本地合并词（按组 token 归属）的`词头 + 词性 + 读音 + 音调 + LLM 释义`，以利于学习。组内无 LLM 词条的词 SHALL 显示词头、读音和已知音调作为回退；一个词由多个 UniDic token 构成时 SHALL 按源 token 顺序展示其音调片段。

#### Scenario: List words inside a group
- **WHEN** 一个组覆盖多个本地合并词（如 ご飯 を 食べた 组）
- **THEN** 组详情除整组 label/释义/语法外，还逐词列出词头、语境词性、读音、音调与 LLM 释义

#### Scenario: Fall back for a word without meaning
- **WHEN** 组内某个本地合并词没有 LLM 词条
- **THEN** 该词在组详情中显示词头、读音与已知音调，不显示伪造的释义或音调

### Requirement: Local fallback preserves readings without successful-analysis underlines
系统 SHALL 在 LLM 不可用（未配置 key、超时、拒绝、畸形 JSON 等）时，仍保留本地合并词的读音和可用的 UniDic 音调标记，但不为失败句段绘制分析成功的下划线。弹窗 SHALL 仅展示词头、读音、音调与界面语言的“无释义”提示，不崩溃、不隐藏本地读音结果，也不进入可禁用云端分析的独立本地模式。未知音调 SHALL 保持无标记状态，并不得阻止其他词显示已知音调。

#### Scenario: LLM unavailable keeps merged readings and pitch
- **WHEN** 短语分析不可用
- **THEN** 本地合并词的振假名和已知音调标记保持可用，失败句段不显示分析成功的下划线，弹窗退化为仅显示词头、读音和音调

#### Scenario: No LLM meaning on offline
- **WHEN** 离线且无 LLM 释义
- **THEN** 弹窗不展示任何从 LLM 推导的释义，本地合并词的读音和音调结果保持可用
