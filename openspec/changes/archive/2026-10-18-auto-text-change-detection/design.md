## Context

KotobaSenpai 目前每次识别都需要用户手动触发"识别"按钮，触发后走 OCR → 分词 → LLM 分析 → overlay 渲染的完整流水线。在 GALGAME 场景中，用户需要频繁切换对话文本，手动点击识别效率低。

同时，LLM 分析是按句子分段并发调用的（`PhraseAnalysisOrchestrator`），每次识别都会重新对所有句子发送 LLM 请求，即使大部分句子与上次完全相同——例如追加一段新台词时，旧台词的 LLM 结果是完全可复用的。

当前架构关键路径：
1. `MainWindowViewModel.RecognizeAsync()` → `WordOverlayApplicationService.RecognizeAndShowAsync()`
2. 该方法依次执行：tracker 验证 → 截屏 → OCR → 分词 → LLM 分析 → overlay 渲染
3. LLM 分析通过 `PhraseAnalysisOrchestrator.AnalyzeAsync()` 按句子并发请求，每个句子生成 `PhraseAnalysisRequest`（含 `SegmentText` 作为天然的缓存 key）
4. overlay 渲染通过 `WpfOverlayRenderer.Show(WordOverlaySession)` 完成

## Goals / Non-Goals

**Goals:**
- 实现基于轮询的文字变化检测，在文字变化时自动触发识别
- 实现句子级 LLM 结果缓存，增量识别时跳过未变化句子的 LLM 调用
- 提供设置开关 `AutoRecognitionEnabled` 控制自动识别
- 保持手动识别流程完全不变

**Non-Goals:**
- 不实现像素级增量 OCR（仅做句子级文本匹配来复用 LLM 结果）
- 不实现跨 session 的持久化 LLM 缓存
- 不改变 LLM 分析的 prompt 或协议
- 不引入截图文件缓存或磁盘持久化

## Decisions

### D1: 变化检测策略 — 像素 diff 粗筛 + OCR 文本精确对比

**选择**: 定时截屏 → 像素差异快速判断 → 有差异时 OCR → 对比 OCR 文本确认是否真正变化

**替代方案**:
- 纯像素 diff：对渲染噪声太敏感（光标闪烁、字幕淡入淡出），误触发率高
- 每次都 OCR 对比：CPU 开销大（本地 ONNX 推理）
- pHash 感知哈希：只能判断画面变了，不知道"哪些文字变了"

**理由**: 像素 diff 粗筛快速排除无变化帧（绝大多数情况），只有画面有变化时才跑 OCR 精确对比文本。平衡性能和准确性。

### D2: LLM 缓存粒度 — 以 `PhraseAnalysisRequest.SegmentText` 为 key

**选择**: 在 `PhraseAnalysisOrchestrator` 内部增加一个内存 `Dictionary<string, PhraseAnalysisResult>` 缓存，key 为句子文本。

**理由**:
- `PhraseAnalysisRequest.SegmentText` 天然就是"这段话的完整文本"，是最精确的缓存 key
- MeikiOCR 是确定性的（同一张图 → 同样的 OCR 结果），所以同一段文字的 token 分割也是稳定的
- 缓存放在 orchestrator 层，不需要改动上层调用逻辑
- 只缓存成功的 `PhraseAnalysisResult`（`Succeeded == true`），失败的不缓存

**风险**: Token ID 对齐 —— 同一句话两次 OCR 的 token 框可能有微调。缓解：MeikiOCR 的检测+识别 ONNX 模型在相同输入下是确定性的，所以 token 分割稳定；即使有微调，现有的 `PhraseGroupValidator` 已有容错机制。

### D3: 增量识别策略 — 文本级前缀/子串匹配

**选择**: 新 OCR 文本与旧文本做比较：
- 旧文本是新文本的子串 → 追加模式：整段合并后重新 LLM 分析（但已缓存的句子跳过）
- 无包含关系 → 全变模式：清空缓存，全新识别

**替代方案**:
- 坐标级增量 OCR：复杂度极高，需要对齐新旧帧的每个字符框，ROI 不高（OCR 本身很快）
- diff-match-patch 算法：过度设计，galgame 追加场景基本是"前面不变，后面加新句子"

**理由**: 覆盖 galgame 90%+ 的追加场景，实现简单可靠。整个文本作为 LLM prompt 更有利于 LLM 理解上下文。

### D4: 轮询机制 — `DispatcherTimer` 定时截屏

**选择**: 复用 WPF `DispatcherTimer`（与现有 hover 轮询机制一致），默认间隔 300ms。

**理由**:
- 与现有 `WpfOverlayRenderer` 的 hover poll 机制一致，熟悉且稳定
- 300ms 足以覆盖人类阅读速度（~200ms/字），同时不会给 CPU 带来压力
- 像素 diff 计算本身很快（< 5ms），不会阻塞 UI 线程

### D5: 自动识别的生命周期管理

**选择**: `AutoRecognitionController` 在 Core 层定义端口，在 Platform.Windows 实现。start/stop 由 settings 开关控制，也支持手动触发。

**理由**: 保持 Core 层不依赖平台具体实现，符合现有 DDD 分层架构。

## Risks / Trade-offs

- **[风险] 像素 diff 误触发** → 使用可配置的差异阈值，大面积变化（如场景切换）不触发（因为文本区域应该小面积变化）
- **[风险] 缓存占用内存** → 设置 LRU 上限（50 条），galgame 场景下足够
- **[风险] 自动轮询与手动识别冲突** → 自动轮询检测到变化时会触发新一轮识别，如果用户同时在手动操作，用 generation counter 保护（已有此机制）
- **[Trade-off] 整段合并 vs 仅发送新句子** → 选择整段合并：LLM 需要上下文才能给出好的翻译，但通过缓存避免了重复调用
