## ADDED Requirements

### Requirement: 自动识别由设置开关控制
系统 SHALL 提供一个布尔设置项 `AutoRecognitionEnabled`（默认 false），用于控制自动轮询识别功能的开关状态。

#### Scenario: 设置开关默认关闭
- **WHEN** 用户首次使用应用且未修改 `AutoRecognitionEnabled` 设置
- **THEN** 自动识别功能 SHALL NOT 运行，用户必须手动点击识别按钮

#### Scenario: 用户开启自动识别
- **WHEN** 用户在设置中将 `AutoRecognitionEnabled` 设为 true
- **THEN** 系统 SHALL 在当前识别会话激活后启动自动轮询检测

#### Scenario: 用户关闭自动识别
- **WHEN** 用户在设置中将 `AutoRecognitionEnabled` 设为 false
- **THEN** 系统 SHALL 停止自动轮询，但不终止当前正在运行的识别会话

#### Scenario: 设置在应用重启后生效
- **WHEN** 应用启动且 `AutoRecognitionEnabled` 为 true
- **THEN** 在用户选择目标窗口并完成首次手动识别后，自动轮询 SHALL 自动启动

### Requirement: 像素差异变化检测
系统 SHALL 以可配置的间隔（默认 300ms）对识别区域进行截屏，并与上一帧的截屏结果进行像素级差异比较。当差异超过阈值时，系统 SHALL 触发一次 OCR 文本确认。

#### Scenario: 无变化时不触发识别
- **WHEN** 连续两帧的像素差异低于阈值
- **THEN** 系统 SHALL 不触发 OCR，不启动新一轮识别

#### Scenario: 有变化时触发 OCR 确认
- **WHEN** 连续两帧的像素差异超过阈值
- **THEN** 系统 SHALL 对当前帧执行 OCR，并将 OCR 结果与上一次已识别的文本进行对比

#### Scenario: OCR 确认文本未变化时跳过
- **WHEN** 像素差异超阈值但 OCR 文本结果与上次完全相同
- **THEN** 系统 SHALL 不触发 LLM 分析和 overlay 更新，保持当前状态

#### Scenario: OCR 确认文本已变化时启动识别
- **WHEN** 像素差异超阈值且 OCR 文本结果与上次不同
- **THEN** 系统 SHALL 启动新一轮识别流程（全变或增量，取决于文本关系）

#### Scenario: 目标窗口不可用时暂停检测
- **WHEN** 目标窗口被最小化、遮挡或关闭
- **THEN** 系统 SHALL 暂停轮询检测，直到目标窗口恢复可用

### Requirement: 全变模式识别
当新旧 OCR 文本之间没有包含关系时，系统 SHALL 执行全变模式识别：关闭当前 overlay，清空 LLM 缓存，从头执行完整的识别流程。

#### Scenario: 文本完全不同
- **WHEN** 新 OCR 文本与旧文本无包含关系（非子串/超集）
- **THEN** 系统 SHALL 关闭旧 overlay、清空 LLM 缓存，并执行完整的 OCR + 分词 + LLM 分析 + overlay 渲染流程

#### Scenario: 全变模式清空缓存
- **WHEN** 系统进入全变模式识别
- **THEN** 之前的 LLM 结果缓存 SHALL 被完全清空，不复用任何旧结果

### Requirement: 增量模式识别
当新 OCR 文本包含旧文本时，系统 SHALL 执行增量模式：保留旧 overlay 中未变化部分，仅对新内容执行 LLM 分析。

#### Scenario: 追加新台词
- **WHEN** 旧文本"今日はいい天気ですね"存在，新文本为"今日はいい天気ですね。散歩に行きましょう"
- **THEN** 系统 SHALL 检测到旧文本是新文本的前缀，以增量模式处理

#### Scenario: 增量模式合并文本后分析
- **WHEN** 进入增量模式
- **THEN** 系统 SHALL 将完整的新文本按句子分段，对每个分段检查 LLM 缓存

#### Scenario: 缓存命中跳过 LLM
- **WHEN** 分段文本"今日はいい天気ですね"在 LLM 缓存中存在
- **THEN** 系统 SHALL 直接使用缓存结果，SHALL NOT 发送 LLM 请求

#### Scenario: 缓存未命中调用 LLM
- **WHEN** 分段文本"散歩に行きましょう"不在 LLM 缓存中
- **THEN** 系统 SHALL 发送 LLM 请求分析该分段，并将结果写入缓存

#### Scenario: 增量模式合并渲染
- **WHEN** 所有分段处理完成（缓存命中 + 新 LLM 结果）
- **THEN** 系统 SHALL 合并所有分段的结果，刷新 overlay 显示完整内容

### Requirement: 轮询间隔可配置
系统 SHALL 支持配置自动识别的轮询间隔（毫秒），默认值为 300ms，最小值 SHALL 不低于 100ms。

#### Scenario: 使用默认轮询间隔
- **WHEN** 未配置轮询间隔
- **THEN** 系统 SHALL 使用 300ms 作为轮询间隔

#### Scenario: 配置自定义轮询间隔
- **WHEN** 用户将轮询间隔设为 500ms
- **THEN** 系统 SHALL 以 500ms 间隔进行截屏检测

#### Scenario: 轮询间隔下限保护
- **WHEN** 用户尝试将轮询间隔设为低于 100ms
- **THEN** 系统 SHALL 将实际间隔钳制为 100ms
