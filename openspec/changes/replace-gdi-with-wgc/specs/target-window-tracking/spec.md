## ADDED Requirements

### Requirement: Separate capture eligibility from overlay visibility
目标快照 SHALL 独立提供捕获资格与依赖目标 UI 的显示资格。有效、可见且未最小化的目标 SHALL 被视为具有捕获资格，遮挡与前台状态 SHALL NOT 取消该资格；实际客户区和 WGC 支持状态由捕获入口另行验证。依赖目标的 UI SHALL 额外要求目标未被遮挡才能显示。tracker SHALL 继续发布 `IsOccluded` 和 `IsForeground`，保留现有事件合并、辅助 HWND 排除、owner/Z 顺序和生命周期规则，不为本次改造新增 tracker 常驻轮询。

#### Scenario: Occluded target remains eligible for capture
- **WHEN** 有效、可见且未最小化的目标被另一个窗口遮挡
- **THEN** 快照的 `IsCapturable` 为 true、`IsRenderable` 为 false，手动识别和自动采样可以继续，overlay 和区域选择 UI 隐藏

#### Scenario: Foreground focus is independent of both predicates
- **WHEN** 目标失去前台焦点但仍有效、可见、未最小化且无遮挡
- **THEN** `IsCapturable` 和 `IsRenderable` 均为 true，不因 `IsForeground` 为 false 停止截图或隐藏现有会话

#### Scenario: An unavailable target is neither capturable nor renderable
- **WHEN** 目标无效、不可见或已最小化
- **THEN** `IsCapturable` 和 `IsRenderable` 均为 false，捕获暂停且依赖该目标的 UI 隐藏

#### Scenario: Occlusion changes do not directly start recognition
- **WHEN** tracker 因遮挡开始或结束而发布新快照
- **THEN** 仅更新依赖目标 UI 的隐藏、恢复和几何状态，不直接调用 OCR 或 LLM；已独立启用的自动识别仍按采样和像素变化规则运行

#### Scenario: Do not mistake a click-through layered helper for a solid occluder
- **WHEN** 目标上方的辅助窗口同时具有 `WS_EX_LAYERED` 和 `WS_EX_TRANSPARENT`，其外框矩形与目标相交
- **THEN** tracker 不将该辅助表面的整个外框当作实心遮挡，不因其存在隐藏目标浮层；普通窗口或仅具有分层/工具窗口样式的窗口仍按既有相交规则判断遮挡
