# Keep local JMdict word merge; LLM supplies per-word contextual meanings

## Why

本地 JMdict 的唯一价值不只是英文释义——它已由 `TokenBoundarySpanResolver` 在 UniDic 边界上做词典最长匹配合并,产出**确定性、离线可用**的词边界 + 词头 + 读音,这是下划线与悬停的结构主单位。而其英文释义(日→英单语、多义项)在"语境单义"诉求下冗余且无法推广多语言。现有 LLM 句段分析调用已携带本地合并词摘要(`LocalSpans`),让其顺带返回**每词语境最佳中文释义 + 语法**只增加输出 token,不增加往返。同时当前悬停一个 `ParsedPhraseGroup` 只显示整组信息,组内单词语义不暴露,不利于学习。

## What Changes

- **保留**本地 jmdic 的 token→word 合并(`TokenBoundarySpanResolver` → `LookupSpan` → `GroupedWord`),词头 + 读音仍本地确定、离线可用。**不**执行旧提案"删除 jmdic 全栈"。
- **替换**悬停释义来源:弹窗不再渲染 JMdict 英文 gloss,改为展示本地词头 + 读音 + LLM 返回的**每词语境最佳中文释义 + 语法**。jmdic 英文释义不再作为弹窗语义来源。
- **新增** LLM 词级输出 `words[]`:在现有那次句段分析响应里,与 `groups[]` 平行返回一个数组。每个词 SHALL 引用请求内已有的**本地合并词**(按 `LocalSpan` 索引),返回该词在语境中的最佳中文释义、语法说明与**语境词性**(如 自動・カ変)。LLM **不返回** tokenIds/headword/reading(本地已可确定)。
- **增强**悬停 UI:选中/悬停一个 `ParsedPhraseGroup` 时,组详情除整组 label/释义/语法外,还逐词列出组内每个本地合并词的**词头 + 词性 + 读音 + LLM 释义**,利于学习。
- **改**组呈现:不再为语法组合组绘制独立上划线;悬停/选中一个组时,改为高亮覆盖该组 parts 的本地合并词下划线,并显示组详情。

## Capabilities

### New Capabilities
- `llm-word-meanings`: LLM 在现有句段分析调用中返回每本地词的最佳语境中文释义、语法与语境词性(`words[]`),按本地合并词索引引用;驱动悬停弹窗与组详情内的逐词语义展示。

### Modified Capabilities
- `english-dictionary`: "Dictionary popup on hover" 的释义来源由本地英文 gloss 改为 LLM 词义(llm-word-meanings);jmdic 保留索引加载、按 token 查词、合并 span 查找,仍提供词头/读音。英文释义不再作弹窗语义。
- `llm-phrase-groups`: 组呈现改为"悬停时高亮成员词下划线",不再绘制独立上划线(新增呈现需求)。

## Impact

- **新增**:`ParsedWordMeaning` 模型、`PhraseAnalysisResult.Words`、`PhraseResponseParser.ParseWords`、`PhraseGroupSchema` 根对象加 `words` 数组、新词义校验器、`PhrasePopup` 组内逐词列表渲染、相应的 i18n 键(`Llm.WordsInstruction`/`WordsLabel`/`WordMeaning` 等)。
- **保留**:`TokenBoundarySpanResolver`、`LookupSpan`、`GroupedWord`(合并)、`JmdictSqliteRepository`、`JmdictLookupService`、`SentenceTokenBuilder` 的 LocalSpans 生成、`PhraseAnalysisRequest.LocalSpans`(LLM 需据此引用本地合并词)。
- **修改**:`PhrasePromptBuilder`(加词级输出指令)、`DeepSeekPhraseAnalyzer`(解析 words 通道)、`PhraseAnalysisOrchestrator`(校验词义)、`DictionaryPopup`(释义源改 LLM)或复用 `PhrasePopup` 机制、`WpfOverlayRenderer`(悬停接线)。
- **不依赖变更**:旧提案新增的任何依赖均不放回。