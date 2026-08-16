# Design: Add draggable recognition-region selector

## Context

当前 `WordOverlayApplicationService.RecognizeAndShowAsync` 对整窗 OCR,`MeikiOcrWordRecognizer` 整窗跑,`WpfOverlayRenderer` 是**点击穿透**的 word-overlay(不接收鼠标)。区域选择需要一个**交互式**遮罩(要拖角、点按钮),所以是新的、非点击穿透的窗口,与 word-overlay 分开。

区域应用:OCR 整窗跑,识别结果(帧坐标字符框)已知;区域只需**过滤**落在区域内的词,不必重做 OCR 或裁剪帧。

区域持久化:存窗口相对、归一化(0-1)矩形,窗口尺寸无关,换窗口也能用。

## Goals / Non-Goals

**Goals:**

- 交互式区域选择遮罩:四角可拖拽的直角框 + 半透明遮罩 + 确定按钮。
- 区域钳制在窗口内、有最小尺寸。
- 确定后持久化区域,识别时按区域过滤识别词。
- 主 UI 入口重新打开区域选择。

**Non-Goals:**

- 不做裁剪/二次 OCR(只过滤识别结果)。
- 不做按窗口的区域 profile(单一全局区域,归一化适用于任意窗口)。
- 不改 `WpfOverlayRenderer`/`IOverlayRenderer`(word-overlay 不变)。
- 不做区域命名/多预设。

## Decisions

### D1. 区域选择器是新的交互式遮罩窗口,与 word-overlay 分离

理由:word-overlay 是点击穿透的只读覆盖;区域选择需要捕获鼠标(拖角、点按钮)。复用 `WpfOverlayRenderer` 的窗口机制(透明、置顶、无激活)但**不禁用命中测试**(非 click-through),新增 `RegionSelectorWindow`(或 `RegionMaskOverlay`),对齐到目标窗口边界。
备选:塞进 `WpfOverlayRenderer` → 否决(职责不同,且需要切换点击穿透状态,复杂)。

### D2. 区域窗口相对、归一化 0-1 持久化

理由:窗口尺寸千差万别,归一化让同一区域适用于任意窗口(换窗口不重拉)。存 settings 键(如 `RecognitionRegion` = `x,y,w,h` 四个 0-1 小数字符串)。打开遮罩时按 `归一化 × 当前窗口尺寸` 初始化四框,超出窗口则钳制。
备选:存绝对像素 → 否决(换窗口/窗口缩放会错位)。

### D3. 捕获层直接抓区域再 OCR,坐标加回偏移 —— 性能优先

理由:OCR 检测成本 ∝ 图像面积、识别成本 ∝ 文字行数。VN 典型用法(识别对话气泡)里,整窗含大量无关 UI/旁白文字,只识别区域能显著减少检测面积与行数,OCR 明显变快(小区域时可达数倍)。做法:**捕获层(`IWindowFrameCapture`)直接只抓区域那一块屏幕像素**(GDI `CopyFromScreen` 区域矩形),不做"整窗抓下再内存裁剪";对区域帧 OCR;把结果字符框坐标**加回区域偏移**(`+region.X/Y`)还原到整窗帧坐标 → 后续分词/屏幕映射不变。坐标偏移是纯加法,无漂移。
区域由 `WordOverlayApplicationService` 从 settings 读取(归一化)换算成帧像素矩形,传给 `IWindowWordRecognizer.RecognizeAsync`(新增可选区域参数);识别器把区域传给捕获层、偏移还原,并回报整窗帧尺寸。
备选:整窗 OCR 后按词框过滤 → 否决(仍付整窗 OCR 全成本,吃不到性能红利)。

### D4. 交互式遮罩的鼠标处理

理由:四角拖拽 + 确定按钮需要命中测试。`RegionSelectorWindow` 用 WPF 鼠标事件(MouseDown/Move/Up)命中角(角命中区),拖拽更新区域并钳制;中间确定按钮点击 → 持久化 + 关闭。遮罩用半透明纯色叠在窗口上,区域内部(四框之间)或不遮罩,提示覆盖范围。
备选:全局鼠标钩子 → 否决(过度,窗口内 WPF 事件足够)。

## Risks / Trade-offs

- [换窗口区域仍适用但可能不理想] → 归一化 + 钳制兜底;用户在必要时重拉(YAGNI per-window profile)。
- [交互遮罩与目标窗口重叠时误点] → 遮罩窗口置顶、无激活,但捕获鼠标;确定按钮在区域内,避免误触目标窗口。
- [裁剪后区域外词不可见] → 明示为设计意图(用户只想要区域内);代价是区域外文字不再可查。
- [极小区域裁剪后 OCR 质量下降] → 低于最小尺寸时回退整窗(见 Open Questions)。

## Migration Plan

1. 新增 `RecognitionRegion` 模型 + settings 键(默认全窗)。
2. 新增 `RegionSelectorWindow`(四角拖拽 + 遮罩 + 确定),`IOverlayRenderer` 旁新增 `IRegionSelector` 端口或直接复用。
3. `WordOverlayApplicationService` 识别时按区域过滤词。
4. 主 UI 加"设置识别区域"按钮 + 命令 + i18n。
5. 回滚:每步独立提交;区域未设置时行为等同现状(全窗)。

## 已定决策

- **不设"全窗"按钮**:用户把四角拖到窗口边缘即可覆盖整窗(钳制到窗口边界天然支持),无需单独按钮。
- 确定按钮位置:区域内居中。
- **裁剪放捕获层**:`IWindowFrameCapture` 直接只抓区域屏幕像素,识别器不再内存裁剪(少一次整窗拷贝 + 少一次内存裁剪)。

## Open Questions

- 裁剪的最小尺寸:区域过小(如 < 整窗 1/20)时裁剪后 OCR 可能退化,是否兜底为整窗或忽略区域?倾向:低于最小尺寸时回退整窗。
