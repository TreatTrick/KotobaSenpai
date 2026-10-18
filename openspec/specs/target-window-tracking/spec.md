# target-window-tracking Specification

## Purpose

持续跟踪当前选中的 Win32 顶层窗口，让覆盖层和区域选择器使用最新的客户区几何与可见层级，并独立发布捕获资格与显示资格。

## Requirements

### Requirement: Event-driven target tracking

系统 SHALL 为当前选中的有效 HWND 注册 Win32 WinEvent hook，并监听窗口位置/大小变化、最小化/恢复、显式显示/隐藏、销毁以及前台窗口变化。系统 SHALL 过滤到目标窗口本身的窗口事件，在收到事件后重新查询客户区矩形、屏幕原点、DPI、可见性、最小化状态、前台状态和目标上方的 Z 顺序。系统 SHALL 使用事件合并避免同一批系统事件触发重复重绘，且 SHALL 不使用常驻轮询作为正常路径。前台状态 SHALL 作为快照信息单独发布，不得成为覆盖层在目标可见、未最小化且客户区合并遮挡比例不超过 30% 时恢复显示的必要条件。tracker 事件本身 SHALL NOT 触发 OCR 或 LLM；独立启用的自动识别仍按采样和像素变化规则运行。

#### Scenario: Target window moves or resizes

- **WHEN** 目标窗口触发客户区位置或大小变化事件
- **THEN** 跟踪器在 UI Dispatcher 上发布一个包含最新客户区屏幕矩形和 DPI 的快照，覆盖层与区域选择器使用该快照重绘已有几何，且不触发 OCR

#### Scenario: Target window changes foreground or Z order

- **WHEN** 前台窗口或目标窗口的 Z 顺序发生变化
- **THEN** 跟踪器发布最新的前台和遮挡状态；目标可见、未最小化且合并遮挡比例不超过 30% 时覆盖层不因目标失去焦点而隐藏，比例严格超过 30% 时隐藏覆盖层，显示时仍保持目标相邻的非置顶 Z 顺序

#### Scenario: Target window is minimized or restored

- **WHEN** 目标窗口开始最小化、完成最小化或恢复显示
- **THEN** 跟踪器分别发布不可见或可见状态；相关 UI 在最小化时隐藏，恢复且满足显示资格时按最新矩形恢复最新保存的会话，事件本身不触发 OCR 或 LLM

#### Scenario: Target window is explicitly shown or hidden

- **WHEN** 目标 HWND 收到 `EVENT_OBJECT_SHOW` 或 `EVENT_OBJECT_HIDE`
- **THEN** 跟踪器合并事件并发布重新查询的可见性快照，覆盖层据此隐藏或在满足显示资格时恢复最新保存的会话，事件本身不触发 OCR 或 LLM

### Requirement: Normalize active recognition geometry

系统 SHALL 将一次识别产生的词框、跨行 rect、短语组 part 几何保存为相对于识别时客户区的归一化矩形，并保存识别时的目标客户区作为稳定基准；系统 SHALL 在每次跟踪快照到达时从该稳定基准重新映射到当前客户区的物理屏幕像素。系统 SHALL 不基于上一次已缩放的屏幕矩形重复缩放，避免累计取整误差。识别区域设置 SHALL 继续使用窗口相对归一化矩形。位置、大小、DPI、可见性、遮挡或前台状态变化的 tracker 通知 SHALL 只触发已有几何的重新投影和显示资格更新，不直接执行 OCR 或 LLM 分析；独立自动识别仍可按采样规则运行。

#### Scenario: Move preserves geometry

- **WHEN** 目标窗口只改变屏幕位置而客户区大小不变
- **THEN** 所有词横线、短语组标记和假名以相同的平移量移动，大小保持不变

#### Scenario: Resize preserves normalized geometry

- **WHEN** 目标窗口客户区改变大小
- **THEN** 词框、短语组几何、横线和假名按客户区宽高比例重新计算，且不重新执行 OCR

#### Scenario: DPI changes with a monitor move

- **WHEN** 目标窗口移动到不同 DPI 的显示器或其 DPI 发生变化
- **THEN** 跟踪器重新查询 DPI，渲染器以新的物理像素/DIP 比例重建窗口和文字尺寸，且不重新执行 OCR

### Requirement: Manage tracker lifecycle and failures

系统 SHALL 在选择目标时注册对应 hook,在目标切换、目标清除和应用退出时注销 hook。显式结束覆盖层会话或关闭区域选择器只清理对应的会话和订阅状态；因目标不可见、最小化或合并遮挡比例超过 30% 而临时隐藏 UI SHALL 保留识别会话和订阅，不得因为一次 UI 隐藏而重建或销毁目标跟踪 hook。hook 注册失败、注销失败或回调处理异常 SHALL 被记录为英文诊断信息，不得传播到 WinEvent 回调线程或终止应用。注册失败时系统 SHALL 隐藏可能过期的跟随 UI，并提供可重试状态；系统 SHALL 不静默退回常驻轮询。用于遮挡判断的 overlay、popup 和 selector HWND SHALL 在创建后登记并在关闭时注销；除这些明确登记的辅助 HWND 外，不得按整个进程排除本进程窗口。

#### Scenario: Target handle is destroyed

- **WHEN** 跟踪器收到目标 HWND 销毁事件或查询确认句柄无效
- **THEN** 系统注销 hook、结束当前跟踪会话、隐藏相关 UI、清理依赖目标的识别会话，并允许用户重新选择窗口

#### Scenario: Hook registration fails

- **WHEN** WinEvent hook 无法注册
- **THEN** 系统记录包含错误原因的英文诊断，隐藏依赖实时跟踪的 UI，并向用户提供重试或重新选择入口

#### Scenario: Callback raises an exception

- **WHEN** 单次 WinEvent 回调处理出现异常
- **THEN** 系统捕获并记录该异常，后续事件仍可继续处理，应用主线程不受影响

#### Scenario: Target tracking is temporarily hidden

- **WHEN** 目标窗口不可见、最小化或客户区合并遮挡比例严格超过 30%，但 HWND 仍然有效
- **THEN** 系统保留当前识别会话和 tracker hook，只隐藏依赖目标的显示 UI；可捕获时手动或自动识别仍可更新保存的会话，目标恢复可见、未最小化且合并遮挡比例不超过 30% 后，UI 使用最新快照和最新有效会话恢复，恢复事件本身不触发 OCR 或 LLM

### Requirement: Separate capture eligibility from overlay visibility
目标快照 SHALL 独立提供捕获资格与依赖目标 UI 的显示资格。有效、可见且未最小化的目标 SHALL 被视为具有捕获资格，遮挡与前台状态 SHALL NOT 取消该资格；实际客户区和 WGC 支持状态由捕获入口另行验证。tracker SHALL 将上层有效遮挡矩形裁到目标客户区，以矩形并集面积除以完整客户区面积，只有比例严格超过 30% 时才令 `IsOccluded` 为 true；重叠部分 SHALL NOT 重复计数，分母 SHALL NOT 改为 OCR 选区面积。依赖目标的 UI SHALL 在该标志为 true 时隐藏，30% 及以下仍可显示。tracker SHALL 继续发布 `IsOccluded` 和 `IsForeground`，保留现有事件合并、辅助 HWND 排除、owner/Z 顺序和生命周期规则，不为本次改造新增 tracker 常驻轮询。

#### Scenario: Occluded target remains eligible for capture
- **WHEN** 有效、可见且未最小化的目标客户区被遮挡的并集面积超过 30%
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
- **THEN** tracker 不将该辅助表面的整个外框当作实心遮挡，不因其存在隐藏目标浮层；普通窗口或仅具有分层/工具窗口样式的窗口仍参与遮挡并集面积计算

#### Scenario: Coverage at the threshold remains renderable
- **WHEN** 目标有效、可见且未最小化，客户区被遮挡的并集面积为 30% 或更少
- **THEN** `IsOccluded` 为 false、`IsRenderable` 为 true，不因少量遮挡隐藏整个浮层；任务栏细边缘同样按该比例规则处理

#### Scenario: Combine multiple occluding windows without double counting
- **WHEN** 多个有效上层窗口覆盖客户区，其中部分遮挡区域互相重叠
- **THEN** tracker 只统计客户区内的并集面积；两个互不重叠的 20% 遮挡合计为 40% 并隐藏浮层，两个完全重叠的 20% 遮挡仍为 20% 并允许显示
