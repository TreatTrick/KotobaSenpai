# Tasks: Merge OCR lines by sentence for cross-line words

## 1. 句段切分放宽

- [x] 1.1 `SentenceSegmenter.ShouldBreak` 删除"下一行左移(阅读顺序不可靠)即断"分支;仅保留句末标点 / 大段落间距断开。
- [x] 1.2 确认 `」` 不单独断句(`。」`/`？」` 由句末标点触发)。
- [x] 1.3 更新 `SentenceSegmenter` 相关单测(折行合并、句号/引号/空隙边界)。

## 2. 跨行合并分词(本地)

- [x] 2.1 新增"合并块 → 一次分词 → 分段 offset 映射回各行"的逻辑;`WordGroupingService` 按句段(而非单行)构造合并文本并分词。
- [x] 2.2 `GroupedWord` 从单一 `Bounds` 扩展为 `IReadOnlyList<ScreenRect> Rects`(每行一个;单行词 = 1 个);保留与既有调用方的兼容构造。
- [x] 2.3 `SentenceTokenBuilder` 按合并块 tokenize,把 token offset 分段映射回各行,生成 token 引用(带各行字符框)。
- [x] 2.4 为跨行合并分词写单测(跨行词成一个 GroupedWord、多 rect、offset 映射正确、不同段不合并)。

## 3. 下划线按 rect 生成 + 整词高亮

- [x] 3.1 `WordOverlaySession.Lines` 改为按每个 `GroupedWord` 的每个 rect 生成一条 `OverlayLine`(贴各自行底)。
- [x] 3.2 `WpfOverlayRenderer` 悬停:命中一个词的任一 rect → 高亮该词全部 rect 的下划线 + 弹释义。
- [x] 3.3 更新 overlay 相关测试(跨行词多下划线、悬停任一 rect 整词高亮)。

## 4. LLM 请求按句段并发 + 只带本段 token

- [x] 4.1 确认 `PhraseAnalysisOrchestrator` 按句段并发(复用 `MaxConcurrency`),段边界 = 放宽后的 `SentenceSegmenter`。
- [x] 4.2 每段请求的 token 表只含该段 token(由句段的行范围筛出)。
- [x] 4.3 更新/补充集成测试(多句并发、每段独立 token、跨行词在段内完整)。

## 5. 收尾

- [x] 5.1 全量 `dotnet build` + `dotnet test` 通过。
- [x] 5.2 更新 `docs/` 相关描述(跨行分词 / 句段切分)。
- [x] 5.3 `openspec validate` + `openspec archive` 落定 delta 到主 specs。