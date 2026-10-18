## MODIFIED Requirements

### Requirement: Failure reporting
系统 SHALL 将目标窗口不可见、最小化、销毁、捕获不受支持、捕获失败或超时、OCR 不可用、覆盖层创建失败和实时跟踪失败转换为用户可理解的错误状态，并允许用户重新选择窗口或重试当前窗口。有效、可见且已恢复的目标 SHALL 不因遮挡或失去前台焦点而被拒绝 WGC/OCR；目标不可见、最小化、无效或没有有效客户区时显式识别 SHALL 被拒绝。覆盖层显示不要求目标处于前台，但仍要求目标处于可见、已恢复且未被遮挡状态。窗口状态事件本身 SHALL NOT 自动重新 OCR；独立启用的自动识别 SHALL 按其像素变化规则执行。

#### Scenario: Capture failure
- **WHEN** 目标窗口被最小化、销毁或 WGC 返回失败
- **THEN** 系统隐藏依赖该目标的覆盖层并显示失败原因与重试入口，不终止应用进程，不回退到屏幕截图

#### Scenario: Recognize a covered target
- **WHEN** 用户请求识别有效、可见、已恢复且可由 WGC 捕获的目标，但另一个窗口部分或完全遮挡它
- **THEN** 系统识别目标自身画面，不要求用户移除遮挡，识别结果在遮挡期间保存而不显示浮层

#### Scenario: Reject an unavailable target
- **WHEN** 用户请求识别但目标不可见、已最小化、已销毁或没有有效客户区
- **THEN** 系统不调用 OCR，显示要求恢复目标或重新选择窗口的可操作提示，提示不把移除遮挡列为识别前提

#### Scenario: Capture is unsupported or times out
- **WHEN** WGC 支持检查失败或首个有效帧超时
- **THEN** 系统给出对应本地化提示，保留英文诊断原因并允许重试，不要求用户安装 OCR 模型来修复捕获故障

#### Scenario: Tracking hook failure
- **WHEN** 跟踪 hook 注册或处理失败
- **THEN** 系统隐藏可能过期的覆盖层，记录英文诊断信息，并提供重试或重新选择入口

#### Scenario: Render a visible background target
- **WHEN** 目标窗口可见、已恢复且未被遮挡，但目标没有获得前台焦点
- **THEN** 系统仍可显示当前有效覆盖层会话，不因 `IsForeground` 为 false 而拒绝渲染

## ADDED Requirements

### Requirement: Keep the latest recognition session while occluded
系统 SHALL 允许遮挡期间完成的手动或自动识别更新当前目标保存的识别会话，并保留最新词线、振假名和短语结果。遮挡期间 overlay、popup 和区域选择 UI SHALL 保持隐藏且不接收 hover；解除遮挡时 SHALL 使用最新会话及最新客户区几何恢复显示。显式结束会话或切换目标后，晚到的捕获/OCR/分析结果 SHALL NOT 恢复旧浮层。

#### Scenario: Finish recognition behind another window
- **WHEN** 目标仍被遮挡时识别或短语分析完成
- **THEN** 系统保存当前目标的最新有效结果，不在遮挡窗口上方绘制任何标记或释义弹窗

#### Scenario: Restore the latest background result
- **WHEN** 目标解除遮挡且遮挡期间已经完成新一轮有效识别
- **THEN** 系统显示最新会话而不是遮挡前旧会话，恢复动作本身不发起 OCR 或 LLM 请求

#### Scenario: Discard a result after explicit stop
- **WHEN** 用户已结束会话或切换目标后旧操作才完成
- **THEN** 系统丢弃旧结果，不重新打开旧浮层、不重启旧目标的自动识别

#### Scenario: Keep hover stable during non-geometry notifications
- **WHEN** 浮层已显示且目标仍可渲染，窗口通知未改变目标客户区矩形和 DPI
- **THEN** 系统维护目标相对 Z 顺序并保留当前悬停高亮和释义弹窗，不因重复、前台或层级通知反复隐藏并重新打开弹窗
