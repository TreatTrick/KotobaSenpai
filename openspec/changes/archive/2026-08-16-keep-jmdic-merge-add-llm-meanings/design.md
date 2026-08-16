# Design: Keep local JMdict word merge; LLM supplies per-word contextual meanings

## Context

KotobaSenpai 当前有两条并行的"查词"路径：

1. **本地 jmdic 合并**：`TokenBoundarySpanResolver` 在 UniDic token 边界上做词典最长匹配，合并出 `LookupSpan` → `GroupedWord`（含 `SourceTokens`/`LookupKey`/`Entries`/`HasResolvedLookup`），一条下划线；悬停时 `DictionaryPopup` 展示英文 gloss。
2. **LLM 短语分析**：`DeepSeekPhraseAnalyzer` 每次识别调一次 LLM，`PhraseAnalysisRequest` 携带句段文本、UniDic token 元数据、本地合并词摘要（`LocalSpans`）；返回语法组合组 `groups[]`（`ParsedPhraseGroup`），悬停时 `PhrasePopup` 展示 label/释义/语法。

关键事实：
- jmdic 的**合并**（token→word，词头 + 读音）是本地、确定、离线可用的结构主单位，下划线热区与悬停都依赖它。**这层价值独立于英文释义**。
- jmdic 的**英文释义**是日→英单语、多义项，在"语境单义"诉求下冗余且无法推广多语言。
- LLM 调用已携带 `LocalSpans`（本地合并词摘要），据此返回每词释义只需扩输出，不增往返。
- 当前 `PhrasePopup` 选中一个组只显示整组信息，组内单词语义不暴露。

## Goals / Non-Goals

**Goals:**
- 保留 jmdic 合并栈（`TokenBoundarySpanResolver`、`LookupSpan`、`GroupedWord`、`JmdictSqliteRepository`、`JmdictLookupService`），下划线与词边界保持本地、确定、离线。
- 悬停释义来源从英文 gloss 改为 LLM 每词的语境最佳中文释义 + 语法。
- LLM 在现有那次调用里顺带返回 `words[]`（引用本地合并词，只含释义 + 语法），不返回 tokenIds/headword/reading。
- `PhrasePopup` 组详情新增"组内逐词释义"列表，利于学习。

**Non-Goals:**
- 不删除 jmdic 索引/合并/查词路径（旧提案 `remove-jmdic-llm-word-merge` 作废）。
- 不引入新的本地词典源；离线词义不作为硬需求（离线保留合并词下划线 + 词头读音）。
- 不做 DokiDokiDict 式人工例外表（`_VOCABULARY_FORCED_LEXEMES`）——YAGNI，等真实 OCR 域错误出现再补。
- 不把合并/几何交给 LLM——结构层保持本地确定，LLM 只做语义。

## Decisions

### D1. 保留 jmdic 合并，只改弹窗释义源
理由:合并是确定性的结构层,`GroupedWord` 已带词头/读音,离线可用。砍掉英文 gloss 只影响弹窗显示,不触碰合并本身。`DictionaryPopup`(英文释义)的释义来源改为 LLM 词义,或复用 `PhrasePopup` 机制。
备选:整个删 jmdic(旧提案)→ 否决(失去本地词边界/词头/读音这一级结构,且下划线将回归逐 token,破坏性大)。

### D2. 词级输出 `words[]` 引用本地合并词,含释义 + 语法 + 语境词性
理由:请求已携带 `LocalSpans`(每个:surface|reading|tokenIds)。LLM 按 0 起 span 索引引用即可,无需再给 tokenIds/headword/reading。`words[]` 与 `groups[]` 平行,同一响应、同一调用,只增输出 token,不增往返。合并决策仍由 jmdic 本地确定,零幻觉。
**词性来源**:活用型(カ変/五段/一段/名詞)本地 UniDic `ConjugationType`+`Pos1` 已有;但**及物性(自動/他動)是词典/语境属性,UniDic 没有**。因 LLM 已按语境选"单义",由 LLM 顺带返回完整语境词性(如 `自動・カ変`)最贴合"语境单义"定位;离线时回退到 UniDic 品词。
备选:让 LLM 输出 tokenIds 合并(旧方案)→ 否决(结构决策交 LLM,非确定、离线塌方)。

### D3. 词级输出与语法组分离,各自独立校验
理由:词义(单词语境义)与组合组(表达式多段)语义不同,混在一起困惑模型。响应含 `groups[]`(既有)与 `words[]`(新)两个数组。`words[]` 按"本地合并词索引"校验,无效词单独丢弃。
备选:把每词义塞进 `groups[]` → 否决(语义混淆、校验复杂)。

### D4. 悬停弹窗:本地词头 + 读音 + LLM 释义 + 语境词性
理由:词头/读音本地确定,LLM 补语境义与语境词性。悬停 token → 找其所属本地合并词 → 显示词头 + 词性 + 读音 + LLM 释义 + 语法(若有);无 LLM 词条 → 显示词头 + 读音 + "无释义"。点击穿透、延迟隐藏、切换更新沿用既有弹窗行为。
备选:离线保留英文 gloss → 否决(违背移除英文单语释义的目的)。

### D5. 组详情新增逐词释义列表
理由:学习诉求。`PhrasePopup` 选中一个组时,按组内 token 归属把本地合并词逐个列出:词头 + 读音 + LLM 释义。无 LLM 词条 → 词头 + 读音作为回退。这是渲染层增强,不动结构。
备选:不做 → 可行但丢失用户明确要的"组内单词意义"。

### D6. 不需要"在线 join 下划线"
理由:合并是本地 jmdic 做的,下划线本来就按合并词一条(旧方案因为合并交给了 LLM 才需要 per-token + join)。保留合并后,几何天然是合并词粒度,无需 D5-join。
备选:逐 token + LLM join → 否决(结构层回归不确定)。

### D7. 组呈现:去掉上划线,悬停时高亮成员词下划线
理由:当前 `WpfOverlayRenderer` 用 `_partElements` 在词顶部画 3px 上划线标组,常驻且与下划线双层视觉噪音。改为:不画上划线;悬停/选中一个组时,把覆盖该组 parts 的本地合并词下划线(`_lineElements`)高亮,并显示组详情。平时无组信号,最干净。映射:组 parts 引用 token → 找覆盖这些 token 的本地合并词 → 高亮其下划线。
备选:常驻高亮组色 → 否决(用户选悬停时高亮,静止仍只有普通下划线)。
**重叠组**:同一词可属多个组,多个组在同一条下划线上只有一个高亮色,重叠组的边界不再区分(视觉上接受,原上划线同样无法干净区分重叠)。

## Risks / Trade-offs

- [离线词义消失] → 明示为取舍:离线保留合并词下划线 + 词头读音,无 LLM 释义。若日后需要离线词义,再评估多语言本地词典(量级远超 jmdic,非本变更)。
- [LLM 词义引用错 span/幻觉] → 校验器按 span 索引丢弃无效/重复词;恶意/错误词不渲染。
- [LLM 输出成本上升] → 词级输出逐句吐 span 索引 + 释义,密集句 token 增多;接受(流式缓解首 token 延迟,成本为 token 而非往返)。
- [英文 gloss 数据仍随包] → 若体积敏感,可后续把 jmdic 索引精简为仅词头/读音(合并不需要 senses);YAGNI,暂不作硬需求。
- [组详情逐词列表在长句过宽] → 弹窗已有滚动容器(`ScrollViewer`),列表纵向扩展,不溢出。

## Migration Plan

1. 先加 LLM 词级输出(新增 `words[]` 解析 + 模型 + 校验),此时英文 gloss 弹窗仍在线,双源并存可对比。
2. 把悬停弹窗切到 LLM 词义,`DictionaryPopup` 英文释义停用/改读 LLM。
3. `PhrasePopup` 组详情加逐词释义列表。
4. 收敛:确认 jmdic 合并栈原样保留,仅释义源变化。
5. 回滚策略:每步独立提交;第 1 步纯新增可回退,第 2/3 步各自独立可回退。

## Open Questions

- `words[]` 每句段最大词数上限设为多少?倾向:与组上限(8)解耦,设宽松上限(如 32)防失控响应。
- `DictionaryPopup` 是改读 LLM 词义,还是直接并入 `PhrasePopup` 的渲染路径?倾向:复用 `PhrasePopup` 机制,新增一个词义渲染分支,避免维护两个弹窗窗口类。
- 是否把 jmdic 索引精简为仅词头/读音(去掉 senses)以减小体积?倾向:暂不,保持现状,等体积成为问题再优化。