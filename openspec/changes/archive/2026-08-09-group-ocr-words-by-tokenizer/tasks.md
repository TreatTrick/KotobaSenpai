## 1. Core 模型与分组服务

- [x] 1.1 新增 `OcrLine` 模型（`Core/Models/OcrLine.cs`）：持有该行 `IReadOnlyList<OcrWord>` 及其按顺序拼接的 surface 文本，用于逐行分词。
- [x] 1.2 新增 `GroupedWord` 模型（`Core/Models/GroupedWord.cs`）：`Token Token` + `ScreenRect Bounds`（成员字符框并集）。
- [x] 1.3 新增 `IOcrWordGroupingService` 契约（`Core/Contracts/`）：`IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines)`。
- [x] 1.4 实现 `WordGroupingService`（`Core/Services/`）：逐行对 surface 文本调 `ITokenizer.Tokenize`，用 `[StartOffset, StartOffset+Surface.Length)` 映射成员字符框并求并集生成 `GroupedWord`；跳过标点/空白 token 与无成员字符的 token。
- [x] 1.5 单测：一行多词各成一条线；多字符词的并集为单条宽线；过滤标点/空白；助词（は/を/です）被保留；无成员字符的 token 被跳过。

## 2. 识别器与编排

- [x] 2.1 改 `MeikiOcrWordRecognizer` 返回 `IReadOnlyList<OcrLine>`（保留行结构，替代当前 `SelectMany` 拍平）。
- [x] 2.2 更新调用方 `WordOverlayApplicationService.RecognizeAndShowAsync`：先映射坐标到 `ScreenRect`，再调 `IOcrWordGroupingService.Group` 得到 `GroupedWord[]`。
- [x] 2.3 改 `WordOverlaySession.Start` 接受 `GroupedWord[]`，派生 `Lines` 改为每 `GroupedWord` 一条 `OverlayLine`（位于 `Bounds` 底部内侧）。

## 3. 渲染器：每词一条线 + 悬停变色

- [x] 3.1 改 `WpfOverlayRenderer.Render`：遍历 `session` 的词而非每字符，为每个词画一条 `Border`（宽度 = 词 `Bounds.Width`）。
- [x] 3.2 在渲染器定义颜色常量：默认 `DeepSkyBlue`、悬停 `OrangeRed`。
- [x] 3.3 新增悬停机制：`DispatcherTimer`（~50ms）轮询 `GetCursorPos`（物理像素）对词的 `Bounds` 做包含测试，维护"当前悬停词"；相邻词热区重叠时取中心最近者。
- [x] 3.4 悬停词变化时仅重绘该词对应 `Border` 的 `Background`（默认 ↔ 橙红），其余线不变。
- [x] 3.5 `Show` 时启动定时器、`Hide` 时停止并清空高亮；保持整窗点击穿透，不改 `WS_EX_TRANSPARENT`/`IsHitTestVisible`。

## 4. DI 接线

- [x] 4.1 `App.xaml.cs` 注册 `IOcrWordGroupingService`（依赖已注册的 `ITokenizer`）。
- [x] 4.2 端到端验证：识别 → 每分词词一条线，悬停变色、移出恢复，点击仍穿透到下方窗口。（待 GUI 手动验证，需桌面 + OCR 模型，无法在无头环境验证）
