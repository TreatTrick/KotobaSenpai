## Context

当前目标跟踪器已经通过 WinEvent hook 发布客户区位置、DPI、可见性、最小化、前台和遮挡状态。`WpfOverlayRenderer` 将识别会话保存为屏幕像素坐标，并在目标状态不满足 `IsRenderable` 时隐藏窗口；`IsRenderable` 目前还额外要求目标窗口是前台窗口。遮挡检测从目标窗口的 Z 顺序向上检查可见窗口，但会跳过整个 KotobaSenpai 进程，因此 KotobaSenpai 主窗口覆盖 Aokana 时不会被识别为遮挡。

识别使用 `Graphics.CopyFromScreen`，不能可靠地读取被其他窗口覆盖的内容。因此，屏幕 OCR 必须继续拒绝不可见、最小化、无效或被遮挡的目标；但已经完成的识别结果可以在目标暂时不可见时保留，并在目标恢复后重新投影。覆盖层需要是 WPF 的 Win32 owned top-level window：owner 指向 Aokana，`Topmost` 为 false，窗口本身仍保持透明、不可激活和点击穿透。

## Goals / Non-Goals

**Goals:**

- 将覆盖层绑定到当前 Aokana HWND 的 owner 生命周期和 Z 顺序。
- 将覆盖层显示条件从“目标是前台窗口”改为“目标可见、已恢复且未被遮挡”。
- 目标隐藏、最小化或被遮挡时立即隐藏覆盖层并停止 hover，但保留有效识别会话。
- 目标恢复、移动、缩放或 DPI 变化时，仅使用归一化几何重新投影和渲染现有会话，不自动 OCR 或调用 LLM。
- 目标销毁或切换时清理会话；显式 OCR 仍在捕获前拒绝遮挡目标并提供英文诊断和本地化错误状态。
- 将遮挡判定限定为目标上方可见窗口与目标客户区存在正面积交集，并排除覆盖层及其他明确注册的辅助窗口。

**Non-Goals:**

- 不把 WPF 窗口改造成真正的 `WS_CHILD` 子窗口；owner 关系足以实现目标窗口级联和层级绑定。
- 不通过 `PrintWindow`、Windows Graphics Capture 或其他方式捕获被遮挡的后台内容。
- 不在窗口移动、大小变化、DPI 变化或恢复显示时重新执行 OCR、分词或 LLM 请求。
- 不持久化 HWND、识别会话、词框、假名、短语结果或截图。
- 不检测半透明窗口的真实 alpha 覆盖形状；只要可见窗口与目标有正面积矩形重叠，就视为遮挡。

## Decisions

### 1. Separate capture state from render state

保留 `TargetWindowSnapshot` 中的 `IsVisible`、`IsMinimized`、`IsOccluded` 和 `IsForeground`，但将覆盖层的可渲染判定定义为 `IsVisible && !IsMinimized && !IsOccluded`，不再要求 `IsForeground`。`IsCapturable` 继续使用同一组可见性、最小化和遮挡条件，供 `WordOverlayApplicationService` 在 GDI 捕获前拒绝无效请求；`IsForeground` 只作为诊断、Z 顺序变化和区域选择交互的状态信息。

这样，用户把焦点切回 KotobaSenpai 时，Aokana 仍可见且未被第三方窗口遮挡，已有覆盖层仍然能够显示。若真正有窗口遮挡 Aokana，覆盖层通过 owner/Z 顺序自然位于遮挡窗口下方，同时 renderer 显式隐藏，避免在错误位置残留。

### 2. Use an owned top-level WPF overlay

在 overlay HWND 创建后，通过 `WindowInteropHelper` 将其 owner 设为当前目标 HWND，并保持 `Topmost = false`、`ShowActivated = false`、`WS_EX_NOACTIVATE` 和 `WS_EX_TRANSPARENT`。目标句柄变化时更新 owner；目标销毁时解除订阅、清空会话并关闭覆盖层。使用 owned top-level window 而不是 `WS_CHILD`，因为跨进程的普通 WPF 子窗口会受到父窗口线程、客户区裁剪和输入路由限制，不能稳定覆盖 Aokana 客户区。

每次目标快照变化时，将 overlay HWND 放到目标相邻的非置顶 Z 顺序位置并更新窗口矩形。owner 关系负责目标级联生命周期和层级约束，tracker 事件仍负责主动隐藏/恢复，因为 WPF owner 的隐藏行为不能替代应用内部会话清理、hover 停止和 popup 关闭。

### 3. Treat any positive-area overlap as occlusion

tracker 从目标 HWND 的 `GW_HWNDPREV` 链检查位于目标上方的窗口，过滤已销毁、不可见、最小化和无效矩形窗口；只要矩形满足 `left < target.right && right > target.left && top < target.bottom && bottom > target.top`，就发布 `IsOccluded = true`。不再按进程 ID 排除所有本进程窗口，而是只排除 overlay、phrase popup、region selector 等明确登记的辅助 HWND；KotobaSenpai 主窗口如覆盖目标，应被视为遮挡。

为避免 Z 顺序检查与窗口创建时序竞态，辅助 HWND 在 SourceInitialized 后登记，在关闭时注销。未登记或无法确认属于辅助 UI 的本进程窗口不得被静默跳过。

### 4. Preserve sessions across temporary visibility changes

renderer 对 tracker 快照执行以下状态转换：

- 句柄不匹配、销毁或 tracker 无法继续跟踪：清空 session、词线、假名、短语标记、hover 状态和 popup，并隐藏窗口。
- 目标不可见、最小化或被遮挡：停止 hover、隐藏 popup、隐藏窗口，但保留 session 和归一化几何。
- 目标恢复可见且未遮挡：以最新客户区快照重新投影并渲染保留的 session，不调用识别服务。
- 目标仍可见但移动、缩放或 DPI 改变：从识别时基准的归一化矩形重建所有词线、假名、pitch 和 phrase marker，不使用上一次的屏幕矩形继续缩放。

显式的 `IOverlayRenderer.Hide()` 继续表示用户主动结束当前显示会话，因此清空 session；tracker 驱动的临时隐藏不调用该清理路径。目标恢复后不自动 OCR，用户必须再次点击识别来适应窗口内容重排。

### 5. Extend event coverage without adding polling

tracker 继续使用一个共享的 out-of-context WinEvent callback，并在 Dispatcher 上合并刷新。除位置变化、最小化开始/结束、销毁和前台变化外，补充 `EVENT_OBJECT_SHOW` 与 `EVENT_OBJECT_HIDE`，在目标窗口发生显式显示/隐藏时刷新快照。回调线程只做事件过滤和一次刷新排队；刷新阶段重新查询 HWND 的有效性、客户区、DPI、可见性、最小化、前台和 Z 顺序。

目标恢复或 Z 顺序变化只能触发快照和覆盖层重投影，不触发 OCR。hook 注册、回调、查询和注销异常均在平台边界记录英文日志；失败时隐藏依赖实时几何的 UI，不启用常驻轮询作为静默后备。

### 6. Verify behavior at the Core and Win32 seams

Core 测试覆盖 `IsCapturable`/`IsRenderable` 的前台解耦、临时隐藏保留 session、销毁清理和不自动 OCR。平台测试使用可注入的窗口状态/Z 顺序提供器验证正面积部分遮挡、KotobaSenpai 主窗口遮挡、辅助 HWND 排除、事件合并和显示/隐藏事件。renderer 测试验证 owner/topmost 配置、隐藏后恢复现有几何和 DPI 重投影；Windows 手工 smoke test 覆盖移动、缩放、跨 DPI、半遮挡、完全遮挡、最小化/恢复、焦点切换和目标销毁。

## Risks / Trade-offs

- [owned window 的隐藏/恢复语义可能因 WPF 调用顺序而表现不同] -> 所有临时状态仍由 tracker 快照显式驱动 Hide/Show，并单独管理 session 清理边界。
- [移除全局 Topmost 后，目标上方的 Z 顺序可能受其他工具窗口影响] -> 使用目标相邻的 `SetWindowPos` 和 owner 关系，并用真实 HWND smoke test 验证遮挡窗口始终位于覆盖层上方。
- [窗口移动或缩放后文本内容可能重新排版] -> 只重投影现有几何，不自动 OCR；通过显式再次识别让用户更新结果。
- [透明或异形遮挡窗口的矩形范围可能大于实际覆盖区域] -> 按约定以任意正面积矩形交集视为遮挡，行为确定且与 GDI 捕获限制一致。
- [辅助 HWND 未及时登记可能被误判为遮挡] -> 集中管理 overlay/popup/selector 的登记和注销，并在诊断中记录排除的 HWND。

## Migration Plan

无需迁移持久化数据；现有识别区域已经是归一化格式。实现时先加入状态和几何测试，再替换 tracker/renderer 的状态转换，最后进行 Windows smoke test。若需要回滚，只回滚代码变更即可，不删除用户设置或诊断文件。

## Open Questions

无。目标窗口 owner 关系、前台解耦、正面积遮挡、临时隐藏保留 session、恢复只重定位不 OCR 均已确认。
