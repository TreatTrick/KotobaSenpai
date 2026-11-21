# llm-word-meanings Specification

## Purpose
TBD - created by archiving change keep-jmdic-merge-add-llm-meanings. Update Purpose after archive.
## Requirements
### Requirement: 为每个本地词返回目标语言释义

系统 SHALL 在现有句段分析调用中，除 `groups[]` 外返回平行的 `words[]`，覆盖请求内 `LocalSpans` 的每个本地合并词（按表顺序，包含同句中重复出现的相同表记），包括助词和助动词。每项 SHALL 用 `headword` 逐字复制对应词块的原文，按请求的有效语言生成该词在语境中的单一最佳 `meaning` 和简洁 `grammar`，保留现有日语词性 `pos` 约定，并包含按需语境读音纠偏字段 `reading`。

当请求表内提供的本地读音在当前上下文中已经正确时，`reading` MUST 返回空字符串 `""`；仅当当前语境下该词块的实际读音与本地读音不同（例如纯语境多音字 `額` 读作 `ひたい`、未收录惯用句或活用特殊读音、数词与量词发生音便等）时，模型 SHALL 在 `reading` 中返回覆盖整个 `headword`（含送假名）的纯平假名读音。词项 SHALL 不返回词元标识，不引用请求之外的实体。

#### Scenario: 覆盖全部本地词块并按需返回读音纠偏
- **WHEN** 句段包含 `額`、`に`、`汗`、`を`、`かく` 等本地词块，且本地表为 `額` 提供读音 `ガク`
- **THEN** 每个词块各有一项词级记录，解释字段统一使用目标语言；其中 `額` 返回 `reading: "ひたい"`，本地读音已经正确的其余词返回 `reading: ""`

#### Scenario: 合并词按整体原文引用
- **WHEN** 本地把多个词元合并为 `でも` 或 `頭を垂れる` 等词块
- **THEN** 词项以合并后的原文作为 `headword`，不拆成多项；若本地合并读音已正确（例如经 JMdict 回流得到 `こうべをたれる`），`reading` 返回 `""`

#### Scenario: 同句中同一表记出现多次时分别按语境输出
- **WHEN** 同一句段内先后出现两个 `額`（如“额头”与“金额”）
- **THEN** 模型按本地词块表顺序输出两项 `headword` 为 `額` 的记录，各自携带对应语境的 `meaning`、`grammar` 与独立 `reading`

### Requirement: Bound and validate word output
系统 SHALL 在接受前校验每个词：`headword` 精确匹配请求内一个尚未绑定的本地合并词的 `surface`（同一句段内同 `surface` 出现 `K` 次时，按响应顺序依次绑定到对应出现次序的本地词块，超出 `K` 次的多余重复项被丢弃）、释义/语法/词性的字段长度受限。无效或匹配不到本地合并词的词 SHALL 被单独丢弃，不影响其余有效词、语法组或本地结果。每句段词级输出 SHALL 有宽松的上限，超出时保留前 N 个并记录诊断。

对于词项中的可选字段 `reading`，系统 SHALL 执行独立校验：
- 缺省、为 `null` 或空白字符串时视为未覆盖，沿用本地读音；
- 非空时经片假名转平假名归一，必须满足长度上限（1～64 字符）且全部字符均为合法日语假名（平假名、`ー`、`ゝ`、`ゞ`）；
- 若对应本地词块不含汉字（纯假名/助词）或归一后的 `reading` 与本地读音相同，系统 SHALL 忽略该覆盖并保持本地读音及既有音调；
- 若 `reading` 包含汉字、拉丁字母、空格、标点或超长等非法内容，系统 MUST 仅丢弃该词的读音覆盖并记录诊断警告，同时保留该词有效的 `pos`、`meaning`、`grammar` 及本地读音。

#### Scenario: Drop a word with an unmatched headword
- **WHEN** 一个词的 headword 匹配不到任何本地合并词（模型多算/错位/抄写误差）
- **THEN** 该词被丢弃，其他有效词继续可用，不发生级联错位

#### Scenario: Drop a duplicate headword beyond local occurrences
- **WHEN** 本地句段中某个表记仅出现 1 次，但模型返回了 2 条相同 `headword` 的词项
- **THEN** 仅保留第 1 条绑定到该本地词块，第 2 条被丢弃

#### Scenario: Bind repeated surfaces in occurrence order
- **WHEN** 本地句段中 `額` 分别在词块索引 0 和索引 5 出现两次，且模型按表顺序返回两条 `額`（`reading` 分别为 `ひたい` 和 `""`）
- **THEN** 第 1 条绑定到索引 0 并生效 `ひたい` 覆盖，第 2 条绑定到索引 5 并保持本地读音 `がく`；若模型返回第 3 条 `額` 则丢弃

#### Scenario: Reject invalid reading without dropping valid meaning
- **WHEN** 模型为 `額` 返回了合法的 `pos`/`meaning`/`grammar`，但 `reading` 包含非法字符（如 `"hitai"` 或 `"額(ひたい)"`）
- **THEN** 系统记录告警并丢弃该读音覆盖（回退本地读音与本地音调），但仍保留该词的有效释义与语法说明

#### Scenario: Ignore reading override on pure-kana span
- **WHEN** 模型为纯假名本地词块（如助词 `は`）返回非空 `reading`（如 `"わ"`）
- **THEN** 系统忽略该读音覆盖，保留本地读音及音调

#### Scenario: Cap an oversized response
- **WHEN** provider 返回的词条数超过每句段上限
- **THEN** 系统在验证后保留前 N 个词条并记录诊断警告

#### Scenario: Ignore malformed words individually
- **WHEN** 一个词缺字段或字段类型错误，而另一个词合法
- **THEN** 合法词被保留，畸形词被丢弃，本地词块/spans 保持可用

### Requirement: 悬停弹窗显示目标语言词义

系统 SHALL 在悬停本地合并词时，展示词头、词性、有效读音（以及对应罗马音）、可用音调及可用的目标语言释义和语法说明。有效读音 SHALL 按“经校验的 LLM 语境纠偏 > JMdict 多词唯一命中回流 > UniDic 原读音”的优先级确定；当读音未发生覆盖时，音调继续来自本地 UniDic 并使用既有统一记法（例如 `[2] LH↓L`）；当读音已被 JMdict 或 LLM 覆盖时，音调 MUST 视为未知并显示未知音调回退，不得展示过期 UniDic 音调。悬停查找 SHALL 优先按 `(SegmentId, SpanIndex)` 匹配对应出现次序的模型词项，以正确区分同句同表记多处出现。释义 SHALL 仅在对应句段完整 AI 分析成功并完成最终覆盖层刷新后可用。弹窗标题 SHALL 按界面语言显示。没有有效释义时 SHALL 展示本地词头、本地有效读音、已知音调和按界面语言显示的“无释义”提示；未知音调不得伪造。弹窗位置 SHALL 在词的包围盒下方且不遮挡词，并继续遵守 `phrase-popup-presentation` 的鼠标停留、10 DIP 离开距离及窗口内输入不透传规则。

#### Scenario: 成功后显示目标语言解释
- **WHEN** 法语分析成功并发布后，光标悬停在有词级记录、保留 UniDic 原读音且音调已知的本地词上
- **THEN** 弹窗展示法语释义和语法说明，以及原有词头、词性、读音和音调；界面标题仍按界面语言显示

#### Scenario: 成功后显示目标语言解释与纠偏读音
- **WHEN** 分析成功并发布后，光标悬停在被 LLM 纠偏为 `ひたい` 的 `額` 上
- **THEN** 弹窗展示目标语言释义和语法说明，读音与罗马音显示为 `ひたい | hitai`，音调显示未知而不显示旧 `がく` 的平板音调

#### Scenario: 同句重复词悬停各自显示独立读音与词义
- **WHEN** 同一句先后出现两个 `額`（分别代表“额头”和“金额”），用户先后悬停两处
- **THEN** 前者弹窗展示 `ひたい` 与“额头”释义，后者弹窗展示 `がく` 与“金额”释义，互不串扰

#### Scenario: 合并词完整展示读音和音调
- **WHEN** 悬停由多个 UniDic 词元合成、未触发读音覆盖且有释义的词
- **THEN** 弹窗按源词元顺序展示合并读音和各个音调片段，不只显示第一个词元

#### Scenario: 没有词义时使用本地信息
- **WHEN** 悬停词没有可用的模型词项
- **THEN** 弹窗显示词头、本地有效读音（含 JMdict 回流读音）、已知音调和界面语言的“无释义”提示，不为空白或崩溃

#### Scenario: 等待模型时不提前显示释义
- **WHEN** 对应句段仍在等待模型
- **THEN** 本地振假名和音调继续可用，弹窗只使用本地信息回退

#### Scenario: 失败句段不显示模型释义
- **WHEN** 对应句段的模型分析失败
- **THEN** 弹窗保留本地词头、本地有效读音和已知音调，不显示该句段的模型释义

#### Scenario: 切换悬停目标不残留旧词
- **WHEN** 光标离开悬停词超过现有延迟，或切换到另一个词
- **THEN** 弹窗按原有规则隐藏或更新，不残留上一词的内容

### Requirement: Show per-word meanings within a phrase group
系统 SHALL 在选中/悬停一个语法组合组（`ParsedPhraseGroup`）并展示其详情时，除组级 label、释义与语法外，还逐词列出组内每个本地合并词（按组 token 归属）的`词头 + 词性 + 有效读音 + 音调 + LLM 释义`，以利于学习。其中有效读音 SHALL 反映已生效的 LLM 语境纠偏或 JMdict 回流读音，且读音被覆盖的词 SHALL 不显示过期 UniDic 音调。组内无 LLM 词条的词 SHALL 显示词头、本地有效读音和已知音调作为回退；一个未覆盖读音的词由多个 UniDic token 构成时 SHALL 按源 token 顺序展示其音调片段。

#### Scenario: List words inside a group with effective readings
- **WHEN** 一个组覆盖多个本地合并词（如含被 LLM 纠偏为 `ひたい` 的 `額`）
- **THEN** 组详情除整组 label/释义/语法外，还逐词列出词头、语境词性、最终有效读音（`ひたい`）、有效音调状态与 LLM 释义

#### Scenario: Fall back for a word without meaning
- **WHEN** 组内某个本地合并词没有 LLM 词条
- **THEN** 该词在组详情中显示词头、本地有效读音与已知音调，不显示伪造的释义或音调

### Requirement: Local fallback preserves readings without successful-analysis underlines
系统 SHALL 在 LLM 不可用（未配置 key、超时、拒绝、畸形 JSON 等）时，仍保留本地合并词的读音和可用的 UniDic 音调标记，但不为失败句段绘制分析成功的下划线。弹窗 SHALL 仅展示词头、读音、音调与界面语言的“无释义”提示，不崩溃、不隐藏本地读音结果，也不进入可禁用云端分析的独立本地模式。未知音调 SHALL 保持无标记状态，并不得阻止其他词显示已知音调。

#### Scenario: LLM unavailable keeps merged readings and pitch
- **WHEN** 短语分析不可用
- **THEN** 本地合并词的振假名和已知音调标记保持可用，失败句段不显示分析成功的下划线，弹窗退化为仅显示词头、读音和音调

#### Scenario: No LLM meaning on offline
- **WHEN** 离线且无 LLM 释义
- **THEN** 弹窗不展示任何从 LLM 推导的释义，本地合并词的读音和音调结果保持可用
