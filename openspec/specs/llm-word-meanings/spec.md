# llm-word-meanings Specification

## Purpose
TBD - created by archiving change keep-jmdic-merge-add-llm-meanings. Update Purpose after archive.
## Requirements
### Requirement: LLM returns per-word contextual meaning
系统 SHALL 在现有那次句段分析调用中，除语法组合组 `groups[]` 外，还返回一个平行的词级数组 `words[]`。该数组 SHALL 覆盖请求内本地合并词（`LocalSpans`）中的每一个词块（含助词、助动词），一个都不遗漏。每个词 SHALL 返回一个 `headword`——即该本地合并词 surface 的逐字复制，作为对该合并词的引用——以及该词在语境中的单一最佳中文释义、中文语法说明与语境词性（如 自動・カ変、他動・五段、名詞）。词 SHALL NOT 返回 token id 或读音——这些由本地分析（jmdic 合并 + UniDic）已提供。词 SHALL 引用请求内已有的本地合并词，不引用本地词典之外的实体。

#### Scenario: Return a meaning for every local chunk
- **WHEN** 句段含若干本地合并词（含助词/助动词，如 学校、は、を）
- **THEN** 响应为每个本地合并词各返回一条词条，覆盖全部词块，不遗漏、不挑选

#### Scenario: Reference a local merged span cross-token
- **WHEN** 一个本地合并词横跨多个 UniDic token（如 `で`+`も` 合并为 `でも`）
- **THEN** 该词以合并词块表的 surface（如 `でも`）作为 headword 引用该合并词，而非逐个 token，也不重复 token 元数据

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

### Requirement: Word meaning drives the hover popup
系统 SHALL 在悬停某个本地合并词时，弹出释义小窗，展示该词的`词头 + [词性] + [读音] + [音调] + LLM 最佳中文释义 + 语法`，其中读音和音调来自本地 UniDic 音调片段，音调使用统一 notation（例如 `[2] LH↓L`）。并定位在该词包围盒下方且不遮挡该词。悬停的词若无 LLM 词条，弹窗 SHALL 展示该词的词头、读音、已知音调与“无释义”提示；音调未知时不得伪造红蓝模式。释义 SHALL 仅在对应句段的完整 AI 分析成功并完成最终覆盖层刷新后可用；等待期间的本地振假名和音调显示不得依赖释义结果。弹窗 SHALL 遵守 `phrase-popup-presentation` 的鼠标停留、输入拦截及滚动规则：移向弹窗时保留，进入后离开超过按当前 DPI 换算的 10 DIP 才隐藏，窗口内操作不得透传。

#### Scenario: Hover a word after successful analysis
- **WHEN** 光标悬停在完成 AI 分析且有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头、语境词性、振假名读音、音调 notation、LLM 最佳中文释义与语法说明

#### Scenario: Hover a merged word with meaning
- **WHEN** 光标悬停在有 LLM 词条且由多个 UniDic token 合并而成的本地词上
- **THEN** 弹窗按源 token 顺序展示合并读音和对应音调片段，不只显示第一个 token 的音调

#### Scenario: Hover a word with no meaning
- **WHEN** 光标悬停在没有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头、读音、已知音调，并提示“无释义”，而非空白或崩溃

#### Scenario: Hover during provider wait
- **WHEN** 光标悬停在 AI 仍在等待的本地词上
- **THEN** 本地振假名和音调标记可见，弹窗不展示尚未返回的 LLM 释义并使用本地词头/读音/音调回退

#### Scenario: Hover a failed sentence word
- **WHEN** 光标悬停在 AI 失败句段中的本地词上
- **THEN** 弹窗展示词头、读音和已知音调回退，不展示该句段的 LLM 释义

#### Scenario: Hide or update on hover change
- **WHEN** 光标移出悬停词并停留超过延迟时间，或切换到另一词
- **THEN** 弹窗隐藏，或内容更新为新词的读音、音调和可用释义，不残留旧词内容

### Requirement: Show per-word meanings within a phrase group
系统 SHALL 在选中/悬停一个语法组合组（`ParsedPhraseGroup`）并展示其详情时，除组级 label、释义与语法外，还逐词列出组内每个本地合并词（按组 token 归属）的`词头 + 词性 + 读音 + 音调 + LLM 释义`，以利于学习。组内无 LLM 词条的词 SHALL 显示词头、读音和已知音调作为回退；一个词由多个 UniDic token 构成时 SHALL 按源 token 顺序展示其音调片段。

#### Scenario: List words inside a group
- **WHEN** 一个组覆盖多个本地合并词（如 ご飯 を 食べた 组）
- **THEN** 组详情除整组 label/释义/语法外，还逐词列出词头、语境词性、读音、音调与 LLM 释义

#### Scenario: Fall back for a word without meaning
- **WHEN** 组内某个本地合并词没有 LLM 词条
- **THEN** 该词在组详情中显示词头、读音与已知音调，不显示伪造的释义或音调

### Requirement: Local fallback preserves underlines and readings
系统 SHALL 在 LLM 不可用（未配置 key、超时、拒绝、畸形 JSON 等）时，仍保留本地合并词的下划线、读音和可用的 UniDic 音调标记，弹窗仅展示词头、读音、音调与“无释义”，不崩溃、不隐藏本地结果。未知音调 SHALL 保持无标记状态，并不得阻止其他词显示已知音调。

#### Scenario: LLM unavailable keeps merged underlines and pitch
- **WHEN** 短语分析不可用
- **THEN** 本地合并词的下划线、振假名和已知红蓝音调标记仍全部可见，弹窗退化为仅显示词头、读音和音调

#### Scenario: No LLM meaning on offline
- **WHEN** 离线且无 LLM 释义
- **THEN** 弹窗不展示任何从 LLM 推导的释义，本地合并词的读音和音调结果保持可用
