## Context

- 日语 OCR（meikiocr）是**字符级**识别：`MeikiOcrEngine` 输出 `MeikiLine(Text, Chars)`，`MeikiOcrWordRecognizer` 用 `lines.SelectMany(Chars)` 拍平成 `OcrWord[]`（每字符一个），`CoordinateMapper` 映到屏幕物理像素 → `ScreenWord[]`。
- `WordOverlaySession.Start(target, screenWords)` 的派生属性 `Lines` 目前**每字符一条** `OverlayLine`。
- `WpfOverlayRenderer.Render` 对每条线画一个 `Border`（默认 `DeepSkyBlue`），窗口为完全点击穿透（`WS_EX_TRANSPARENT|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` + `IsHitTestVisible=false`），**无任何鼠标交互**。
- 分词器 `ITokenizer.Tokenize(string)` 已注册 DI（`UniDicTokenizer` / MeCab），`Token` 有 `StartOffset` 但无长度/结束偏移，需用 `Surface` 长度推算 span。当前未接入渲染流程。

## Goals / Non-Goals

**Goals:**
- 建立"把一行 OCR 字符经分词器重新组合成词"的机制（可复用 Core 服务）。
- 覆盖层改为**每分词词一条下划线**；悬停在词包围盒上时该条线下整条线变色。
- 保持整窗点击穿透（不拦截点击），仅捕获悬停。

**Non-Goals:**
- 悬停弹释义/读音气泡、点词取词联动（后续独立功能）。
- 竖排文本、跨视觉行被拆开的词的分组（第一阶段仅横排、按行内成词）。
- 分词器本身的行为/字典改动。

## Decisions

### D1. 按 OCR 行做分词，保证每条线是单个矩形
用每行 OCR 文本独立过 `ITokenizer`，token span 只在该行内映射字符框。这样词不会横跨视觉行，每条下划线始终是单个并集矩形。
- **替代方案**：把整页字符拼成一个字符串再分词 → token 可能跨越换行，并集包围盒是非矩形，需额外拆分逻辑。**弃用**（更复杂且竖排/换行场景边界难控）。

### D2. 保留行结构：`MeikiOcrWordRecognizer` 改为返回行分组
把识别器从 `OcrWord[]`（拍平）改为 `IReadOnlyList<OcrLine>`，`OcrLine` 持有该行 `IReadOnlyList<OcrWord>`。识别器内部本就有 `MeikiLine`，改动最小；分组服务据此逐行分词。
- 分词字符串用该行实际 `OcrWord` 的 surface 顺序拼接（而非信任 `MeikiLine.Text`），保证 span 一定能映射回真实字符框。

### D3. 分组逻辑放 Core（纯逻辑，可单测）
新增 `IOcrWordGroupingService`（`Core/Contracts`）+ `WordGroupingService`（`Core/Services`）：输入行分组的字符 + `ITokenizer`，输出 `GroupedWord[]`。
- 每个 token 映射 `[StartOffset, StartOffset+Surface.Length)` 到字符，取这些字符框的并集 → `GroupedWord(Token, Bounds)`。
- 过滤：跳过标点/空白 token（`ITokenizer` 已跳过 BOS/EOS；此处按 `Surface` 是否为标点/空白判定）；无成员字符的 token 跳过。
- 纯 Core：不依赖 WPF/平台，便于写单测。

### D4. `WordOverlaySession` 改为承接 `GroupedWord`，`Lines` 每词一条
`Start(target, groupedWords)`；派生 `Lines` = 每个 `GroupedWord.Bounds` 一条 `OverlayLine`。`GroupedWord` 携带 `Token`（含 Reading/Lemma），为将来取词预留，成本可忽略。

### D5. 悬停用定时轮询 `Cursor.Position` 命中测试，不复用窗口输入
覆盖层必须保持点击穿透，因此**无法用窗口自身鼠标事件**：OS 把每个鼠标事件只路由给命中测试命中的最上层可接收窗口；点击穿透靠 `WS_EX_TRANSPARENT`/`HTTRANSPARENT` 让覆盖层在命中测试中对自己透明，结果它收不到任何鼠标消息（含 `WM_MOUSEMOVE`）。"收事件"与"透传"在同一窗口的消息层面互斥。因此改为从窗口消息管道之外观察鼠标：采用 `DispatcherTimer`（~50ms）读取 `Cursor.Position`（物理像素），对每个 `GroupedWord.Bounds`（物理像素）做包含测试，维护"当前悬停词"；变化时仅重绘该词对应 `Border` 的 `Background`（默认 `DeepSkyBlue` ↔ 悬停 `OrangeRed`）。
- **替代方案**：全局低层鼠标钩子 `WH_MOUSE_LL`（从事件管道外观察、不吞消息则点击照常穿透）→ 更实时但引入全局钩子安装/清理复杂度。**弃用**，50ms 轮询对手感足够且简单；若轮询有可感知延迟，可换钩子。
- 命中测试在**物理像素**层面进行（与 `Bounds` 同坐标系），渲染时再按 DPI 转 DIP。

### D6. 颜色常量置于渲染器
默认 `DeepSkyBlue`、悬停 `OrangeRed` 作为渲染器常量，不在 Core 模型里硬编码 UI 颜色（保持 Core 平台无关）。

## Risks / Trade-offs

- **MeCab 切分与 OCR 识别不一致**（OCR 认错字、切分结果与字符框不完全对齐、token 过大/过小） → 分组只按 span 聚合真实存在的字符框，map 不到字符的 token 静默跳过；悬停变色不依赖分词准确性，能用。
- **每词一条线合并后热区变大**，相邻词热区可能重叠 → 命中测试取"包含光标且中心最近"的词，避免视觉歧义。
- **轮询定时器常驻** → 仅在覆盖层可见时启动，`Hide()` 时停止并清空高亮；50ms 开销可忽略。
- **悬停变色仅影响视觉，不拦截点击** → 满足"点击穿透"既有约束；若未来要点击取词，再引入热区级命中测试（后续需求）。