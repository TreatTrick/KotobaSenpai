# Design: Merge OCR lines by sentence for cross-line word correctness

## Context

当前 `WordGroupingService.Group` 逐行 `tokenize(line.Text)`,`TokenBoundarySpanResolver` 逐行解析 span,`SentenceTokenBuilder` 逐行建 token 引用。一个横跨两行的词(OCR 把 `世界` 折成 `せ`/`かい` 两行)在本地就是两个残片 token:
- 两个 `GroupedWord`、两条下划线 → 悬浮无法整词高亮;
- 本地没有跨行 span,LLM 返回的 `世界` headword 匹配不到本地残片 → 释义丢失。

根本原因:**分词在行边界上切词,而不是在语义(句子)边界上**。LLM 请求怎么切(A/B)是上层;必须先修本地跨行分词。

## Goals / Non-Goals

**Goals:**
- 本地按句段把多行合并成一个文本块,一次分词;跨行词成为一个 `GroupedWord`。
- `GroupedWord` 支持每行一个 rect 的多 rect 几何;下划线按 rect 生成;悬停任一 rect 高亮整词。
- 句段切分:仅在句末标点(`。！？…‥．`)或大段落间距处断开,去掉"阅读顺序不可靠就断开"。
- LLM 请求按句段并发,每段仅带本段 token。
- 不变量:词不跨段(段边界是标点,不在词里)。

**Non-Goals:**
- 不改 LLM 词义匹配方式(仍按 headword 匹配本地 span)。
- 不做"整窗一个请求"(B)——按句并发(A)更快(并发小请求墙钟短于一次大流式)。
- 不把 `」` 单独当结尾标点(内嵌引用场景会误断)。
- 不改识别区域裁剪/屏幕映射。

## Decisions

### D1. 合并块 = 句段;本地分词在句段上做
理由:本地 span 要和 LLM 请求的 token 一一对应。哪个行块该合并分词,就该发哪个请求。段边界由 `SentenceSegmenter` 决定(句末标点 / 大间距),本地与 LLM 用同一套段。
备选:本地逐行、LLM 按句 → 否决(span 与 token 错位,headword 匹配不上)。

### D2. 跨行分词的实现:拼文本 → 一次分词 → 分段映射 offset
做法:把段内多行文本按顺序拼接(行间不插字符,或插入已知分隔符),一行文本一次 `tokenize`;每个 token 的 `[StartOffset,EndOffset)` 落在拼接文本上,再**分段映射**回原始各行 → 每行一个字符框并集 → 该 token 的 rect 列表。跨行 token 自然得到多个 rect。
坐标:拼接文本的 offset = 行内偏移 + 之前所有行的长度;映射时对每行取 `[offset 与行范围的交]` 的字符框。
备选:跨行专改 span resolver → 复杂且难验证;拼文本一次分词最接近"语义分词"。

### D3. `GroupedWord` 多 rect;下划线按 rect 生成;悬停整词
`GroupedWord` 从单一 `Bounds` 扩展为 `IReadOnlyList<ScreenRect> Rects`(每行一个;单行词 = 1 个)。`OverlayLine` 由每个 rect 生成一条(贴各自行底)。`WpfOverlayRenderer.PollHover` 悬停压中任一 rect → 该词高亮其**全部** rect(整词一起变色)并弹释义。
备选:跨行词画一条竖跨几行的粗线 → 否决(遮挡、不自然)。

### D4. 句段切分放宽
`SentenceSegmenter.ShouldBreak` 改为:前一行末尾是句末标点(`。！？…‥．`)→ 断;或下一行与上一行间隔 > 段落间距因子 → 断;**删除"下一行左移(阅读顺序不可靠)即断"**。`」` 不单独断(内嵌引用 `「彼は「行こう」と` 中途的 `」` 若断会误切)。`。」`/`？」` 已含句末标点,自然断。
备选:完全不管空隙只按标点 → 否决(旁白/对话等不相干块会硬拼,制造新错词)。

### D5. LLM 请求按句段并发,每段只带本段 token
既有 `PhraseAnalysisOrchestrator.AnalyzeAsync` 已对每段并发(`MaxConcurrency=4`)。保持;每段请求的 token 表只含该段 token(由 D1 的段决定)。token id 行感知(`l{line}:t{token}`),段知道自己覆盖哪些行,取其 token。
备选:整区一个请求(B) → 否决(速度:A 并发小请求墙钟短;健壮:一句失败不影响其他句)。

## Risks / Trade-offs

- [跨行合并分词遇到大空隙错拼旁白/对话] → 段按大间距断开,空隙处仍是段边界,不跨段拼词。
- [拼接文本的 offset 映射出错] → 用行长度精确累加 + 单测覆盖跨行 token。
- [多 rect 下划线在跨行词上视觉更碎] → 悬停整词高亮补足;跨行词本身少,可接受。
- [每段请求重复携带 system prompt,开销 ↑] → 每段只带本段 token(不重复整窗),system prompt 重复属并发固有成本,接受。

## Migration Plan

1. `SentenceSegmenter` 放宽断开规则(删顺序不可靠分支)。
2. `WordGroupingService` / `SentenceTokenBuilder` 改为按段拼文本 → 一次分词 → 分段映射 token 到多行 rect。
3. `GroupedWord` / `OverlayLine` / `WordOverlaySession.Lines` 支持多 rect。
4. `WpfOverlayRenderer` 悬停整词高亮(命中任一 rect 高亮全部)。
5. 确认 LLM 请求按段并发、每段只带本段 token。
6. 回滚:每步独立提交;任何一步可回退,不破坏整窗(未合并时退化为逐行)。

## 已定决策

- **大段落间距阈值沿用 `ParagraphGapFactor=1.5`**(不按识别区域缩放)。
- **拼接文本行间不插分隔符**(纯拼接,让分词器按连续文本处理,避免分隔符被当成词边界重新切词)。
- **空 rect 的 `GroupedWord` 跳过**(无字符框不成词,沿用现有 zero-width 跳过)。