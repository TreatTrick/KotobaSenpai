# Tasks: Keep local JMdict merge; LLM per-word meanings; group per-word UI

jmdic 合并栈**原样保留**(`TokenBoundarySpanResolver`/`LookupSpan`/`GroupedWord`/`JmdictSqliteRepository`/`JmdictLookupService`)。本变更只新增 LLM 词级释义并把弹窗语义源从英文 gloss 切到 LLM。

## 1. LLM 词级输出（先建，双源并存可对比）

- [x] 1.1 新增 `src/KotobaSenpai.Core/Models/ParsedWordMeaning.cs`：`{ModelWordId, SpanIndex, Pos, Meaning, Grammar}`，构造时校验非空、SpanIndex >= 0。
- [x] 1.2 扩展 `PhraseResponseParser` 新增 `ParseWords(JsonElement)`：解析 `words[]` 数组（`modelWordId` + `spanIndex` 整数 + `pos` + `meaning` + `grammar` 字符串），结构不符抛 `PhraseResponseException`。
- [x] 1.3 扩展 `PhraseGroupSchema.Root`：根对象 `properties` 增加 `words` 数组（`required` 增加 `words`），项含 `modelWordId`/`spanIndex`/`pos`/`meaning`/`grammar`，`additionalProperties=false`。
- [x] 1.4 扩展 `PhraseAnalysisResult` 增加 `Words` 字段（`IReadOnlyList<ParsedWordMeaning>`），`DeepSeekPhraseAnalyzer.AnalyzeAsync` 一并解析 `words` 通道。
- [x] 1.5 在 `PhrasePromptBuilder` 的 system prompt 指令中说明 `words[]` 输出格式（按 `LocalSpans` 的 0 起索引引用本地合并词，只给释义 + 语法，不给 tokenIds/词头/读音），并新增本地化键（如 `Llm.WordsInstruction`/`Llm.WordsLabel`/`Llm.WordMeaningLabel`）。
- [x] 1.6 为 `PhraseResponseParser.ParseWords` 写单测（合法/缺字段/spanIndex 非整数/非数组）。

## 2. 词级释义校验与接线

- [x] 2.1 在 `PhraseAnalysisOrchestrator` 校验 `ParsedWordMeaning`：`spanIndex` 属于请求内 `LocalSpans`、词内扇面不重复引用同一 span、释义/语法/词性长度上限；无效词单独丢弃，不 abort。
- [x] 2.2 设每句段词数上限（宽松，防失控响应，如 32），超出保留前 N 个并记诊断。
- [x] 2.3 新增 `WordMeaningView`（映射：本地合并词/bounds → 词义），挂到 `WordOverlaySession`；`Session.Start` 接收词义列表。
- [x] 2.4 为词义校验/丢弃/上限写单测（资产：跨 token 合并词、无效 span 索引、重复引用、超限）。

## 3. 悬停弹窗释义源切到 LLM

- [x] 3.1 `WpfOverlayRenderer` 的 local-word 悬停分支改查 `session` 的本地合并词 → 词义映射，弹窗展示词头 + 语境词性 + 读音 + LLM 释义 + 语法；无词义时展示词头 + 读音 + "无释义"。
- [x] 3.2 `DictionaryPopup` 释义源改为 LLM 词义（并入 `PhrasePopup` 渲染路径，删除 `DictionaryPopup` 窗口类），英文 gloss 不再作释义显示。
- [x] 3.3 确认本地合并词下划线/几何不变（jmdic 合并原样保留），仅语义层切换。
- [x] 3.4 更新悬停相关测试：`PhraseSession.TryGetMeaning` 词义映射（有/无词义回退）。

## 4. 组呈现改为成员词下划线高亮

- [x] 4.1 `WpfOverlayRenderer` 移除 `_partElements` 上划线绘制；不再为任何组在词顶画 3px 条。
- [x] 4.2 悬停组时：解析组 parts → 覆盖其 token 的本地合并词 → 将这些词的下划线（`_lineElements`）设为高亮色；离开组时恢复普通色。
- [x] 4.3 处理多组重叠：同一词属多个组时用单一高亮色，不区分重叠组边界。
- [x] 4.4 更新 overlay 渲染测试：`GetCoveredWordIndices` 返回组覆盖的成员词下划线索引。

## 5. 组详情逐词释义列表

- [x] 5.1 扩展 `PhrasePopup` 渲染：选中一个组时，按组内 token 归属把本地合并词逐个列出 "词头 + 语境词性 + 读音 + LLM 释义"；无 LLM 词条的词仅显示词头 + 读音。
- [x] 5.2 组级 label/释义/语法保持展示，逐词列表作为附加块；复用既有 `ScrollViewer` 纵向滚动。
- [x] 5.3 为组内逐词列表写渲染/数据测试（组覆盖多词、某词无词义回退）。

## 6. 本地化与收敛

- [x] 6.1 补齐新增 i18n 键（`Llm.WordsInstruction`/`WordsLabel`/`WordMeaningLabel`/`WordNoMeaning`/`WordPosLabel` 等）到各语言资源。
- [x] 6.2 全量 `dotnet build` + `dotnet test` 通过，确认无删除 jmdic 的残留引用（旧提案作废）。
- [x] 6.3 更新 `docs/` 中相关描述（如 jmdic 定位由"查词+释义"改为"合并+词头读音，释义交 LLM"）。
- [x] 6.4 `openspec validate` + `openspec archive` 落定 delta 到主 specs。