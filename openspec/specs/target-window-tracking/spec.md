# target-window-tracking Specification

## Purpose

持续跟踪当前选中的 Win32 顶层窗口，让覆盖层和区域选择器使用最新的客户区几何与可见层级。

## Requirements

### Requirement: Event-driven target tracking

系统 SHALL 为当前选中的有效 HWND 注册 Win32 WinEvent hook，并监听窗口位置/大小变化、最小化/恢复、销毁以及前台窗口变化。系统 SHALL 过滤到目标窗口本身的事件，在收到事件后重新查询客户区矩形、屏幕原点、DPI、可见性和最小化状态。系统 SHALL 使用事件合并避免同一批系统事件触发重复重绘，且 SHALL 不使用常驻轮询作为正常路径。

#### Scenario: Target window moves or resizes

- **WHEN** 目标窗口触发客户区位置或大小变化事件
- **THEN** 跟踪器在 UI Dispatcher 上发布一个包含最新客户区屏幕矩形和 DPI 的快照，覆盖层与区域选择器使用该快照重绘

#### Scenario: Target window changes foreground or Z order

- **WHEN** 前台窗口或目标窗口的 Z 顺序发生变化
- **THEN** 跟踪器发布可见层级变化，目标窗口上方的 UI 与目标保持相同 Z 顺序，其他窗口遮挡目标时不浮在遮挡窗口上方

#### Scenario: Target window is minimized or restored

- **WHEN** 目标窗口开始最小化、完成最小化或恢复显示
- **THEN** 跟踪器分别发布不可见或可见状态，相关 UI 隐藏/关闭或按最新矩形恢复，且不触发自动 OCR

### Requirement: Normalize active recognition geometry

系统 SHALL 将一次识别产生的词框、跨行 rect、短语组 part 几何保存为相对于识别时客户区的归一化矩形；系统 SHALL 在每次跟踪快照到达时从该稳定基准重新映射到当前客户区的物理屏幕像素。系统 SHALL 不基于上一次已缩放的屏幕矩形重复缩放，避免累计取整误差。识别区域设置 SHALL 继续使用窗口相对归一化矩形。

#### Scenario: Move preserves geometry

- **WHEN** 目标窗口只改变屏幕位置而客户区大小不变
- **THEN** 所有词横线、短语组标记和假名以相同的平移量移动，大小保持不变

#### Scenario: Resize preserves normalized geometry

- **WHEN** 目标窗口客户区改变大小
- **THEN** 词框、短语组几何、横线和假名按客户区宽高比例重新计算，且不重新执行 OCR

#### Scenario: DPI changes with a monitor move

- **WHEN** 目标窗口移动到不同 DPI 的显示器或其 DPI 发生变化
- **THEN** 跟踪器重新查询 DPI，渲染器以新的物理像素/DIP 比例重建窗口和文字尺寸

### Requirement: Manage tracker lifecycle and failures

系统 SHALL 在选择目标时注册对应 hook,在目标切换、目标清除和应用退出时注销 hook。覆盖层隐藏或区域选择器关闭只清理其会话和订阅状态,不得因为一次 UI 隐藏而重建或销毁目标跟踪 hook。hook 注册失败、注销失败或回调处理异常 SHALL 被记录为英文诊断信息，不得传播到 WinEvent 回调线程或终止应用。注册失败时系统 SHALL 隐藏可能过期的跟随 UI，并提供可重试状态；系统 SHALL 不静默退回常驻轮询。

#### Scenario: Target handle is destroyed

- **WHEN** 跟踪器收到目标 HWND 销毁事件或查询确认句柄无效
- **THEN** 系统注销 hook、结束当前跟踪会话、隐藏相关 UI，并允许用户重新选择窗口

#### Scenario: Hook registration fails

- **WHEN** WinEvent hook 无法注册
- **THEN** 系统记录包含错误原因的英文诊断，隐藏依赖实时跟踪的 UI，并向用户提供重试或重新选择入口

#### Scenario: Callback raises an exception

- **WHEN** 单次 WinEvent 回调处理出现异常
- **THEN** 系统捕获并记录该异常，后续事件仍可继续处理，应用主线程不受影响
