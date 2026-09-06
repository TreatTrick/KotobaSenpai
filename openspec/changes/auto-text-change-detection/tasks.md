## 1. LLM 结果缓存

- [x] 1.1 在 `PhraseAnalysisOrchestrator` 中增加内存字典缓存 `SegmentText` → `PhraseAnalysisResult`
- [x] 1.2 缓存命中时跳过 LLM 请求，直接返回缓存结果
- [x] 1.3 仅缓存 `Succeeded == true` 的结果，失败结果不缓存
- [x] 1.4 设置缓存容量上限（50 条），满时 FIFO 淘汰
- [x] 1.5 提供 `ClearCache()` 方法供全变模式调用

## 2. 自动识别控制器

- [x] 2.1 在 Core 层定义 `IAutoRecognitionController` 端口（Start/Stop/IsRunning）
- [x] 2.2 在 Platform.Windows 实现 `AutoRecognitionController`，使用 `DispatcherTimer` 轮询
- [x] 2.3 实现像素差异检测：截取识别区域 → 与上一帧对比 → 差异超阈值时触发
- [x] 2.4 轮询间隔默认 300ms，可配置，下限 100ms
- [x] 2.5 目标窗口不可用时暂停轮询，恢复后继续

## 3. 增量识别集成

- [x] 3.1 在 `WordOverlayApplicationService` 中增加增量识别入口
- [x] 3.2 新 OCR 文本与旧文本比较：全变 → 清缓存 + 全量识别；追加 → 增量识别
- [x] 3.3 增量模式下，合并新旧文本后分段，逐段检查 LLM 缓存
- [x] 3.4 增量模式保持上一轮 overlay 不变，全部处理完成后一次刷新

## 4. 设置与 UI

- [x] 4.1 在 `MainWindowViewModel` 中增加 `AutoRecognitionEnabled` 设置读写
- [x] 4.2 在主窗口 UI 增加自动识别开关复选框
- [x] 4.3 开关切换时启动/停止自动识别控制器

## 5. 端到端验证

- [x] 5.1 验证手动识别流程不受影响
- [x] 5.2 验证自动识别开启后文字变化时自动触发
- [x] 5.3 验证增量模式下 LLM 缓存命中跳过了重复请求
- [x] 5.4 验证全变模式清空缓存并重新完整识别
