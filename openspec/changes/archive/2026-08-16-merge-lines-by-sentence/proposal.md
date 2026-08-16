# Merge OCR lines by sentence for cross-line word correctness

## Why

当前分词与 LLM 请求都**逐行**进行:一个横跨两行的词被切成两个残片,导致 (1) 两半意思都不对、(2) 该词永远无法在悬浮时整词高亮。根本原因是本地分词在行边界上切词,而非在语义(句子)边界上。

## What Changes

- **本地跨行分词**:把同一句段(按句末标点断开的连续行块)的多行**拼成一个文本**,一次性分词;token 的 offset 分段映射回各行的字符框,跨行词成为一个 `GroupedWord`,携带**每行一个 rect** 的几何。
- **`GroupedWord` 多 rect**:一个词可含 1 个 rect(普通词)或 N 个 rect(跨行词,各贴自己行);`OverlayLine` 按 rect 生成(一个词 N 条下划线),悬停任一段高亮整词。
- **句段切分放宽**:`SentenceSegmenter` 只在**句末标点(`。！？…‥．`)或大段落间距**处断开;**不再因"阅读顺序不可靠"断开**。`」` 不单独断句(`。」`/`？」` 已由句末标点触发)。
- **LLM 请求 = 按句段并发**:每段一个请求、复用现有并发(`MaxConcurrency`);每段请求**只携带本段 token 表**。
- **不变量**:词不跨段(段边界是句末标点,标点不属于词),段内 token 连续。

## Capabilities

### New Capabilities
-(无全新能力;以下为对既有能力的行为级修改)

### Modified Capabilities
- `word-grouping`: 本地分词从逐行改为按句段跨行合并;`GroupedWord` 支持多 rect 几何。
- `window-word-overlay`: 下划线按词内每 rect 各生成一条;悬停任一 rect 高亮整词。
- `japanese-tokenizer`: 分词输入从单行文本扩展为"多行合并文本 + 分段 offset 映射"(由 grouping 层消费)。
- `llm-phrase-groups`: 句段切分规则放宽(仅句末标点 / 大间距断开),请求粒度 = 句段并发,每段仅带本段 token。

## Impact

- **修改**:`SentenceSegmenter`(放宽断开)、`WordGroupingService`(跨行合并分词 + 多 rect)、`GroupedWord`/`OverlayLine`(多 rect)、`SentenceTokenBuilder`(按合并块 tokenize + 分段映射 token + 只发本段 token)、`WpfOverlayRenderer`(悬停整词高亮)、`WordOverlaySession.Lines`(按 rect 生成)。
- **保留**:`CoordMapper` 屏幕映射、识别区域裁剪、LLM 并发调度、`llm-word-meanings` 按 headword 匹配。
- **测试**:跨行合并分词、多 rect 几何、整词高亮、句段切分歧义(句号/引号/空隙)。