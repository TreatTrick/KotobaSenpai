## Why

当前 OCR 使用 GDI `CopyFromScreen` 读取目标窗口对应的桌面区域，遮挡物会进入截图，因此系统只能拒绝被遮挡目标的识别。用户需要在目标窗口仍展开但被其他窗口覆盖时继续读取该窗口自身的文字；第一阶段预留的 WGC 替换应在本次落地。

## What Changes

- 使用 `Windows.Graphics.Capture` 和 `CreateForWindow(HWND)` 替换 GDI，手动识别与自动变化检测统一读取选中窗口的画面；不自动回退到桌面截图。
- 允许有效、可见、未最小化的目标在失去前台焦点、部分遮挡或完全遮挡时继续被采集；最小化、隐藏、销毁及捕获不可用时停止提供帧。
- 分离截图资格和浮层显示条件：遮挡不阻止 WGC/OCR；多个上层窗口在客户区内的遮挡并集面积超过完整客户区的 30% 时才隐藏下划线、振假名、释义弹窗和区域选择 UI，30% 及以下仍可显示。
- 保持客户区物理像素、归一化选区、BGRA 输入和 OCR 坐标契约；正确处理 WGC 窗口画面中的标题栏、边框、DPI 和尺寸变化。
- 增加可复用的捕获会话、有限等待、取消和确定性资源释放；不让 WGC 的每帧回调直接运行 OCR 或 LLM。
- 更新识别失败提示、自动识别的遮挡策略及相关测试。接受系统捕获边框，不在本次实现去边框授权、最小化窗口实时识别、受保护内容捕获或目标程序后台渲染干预。

## Capabilities

### New Capabilities

- `window-frame-capture`: 按 HWND 捕获不含其他顶层遮挡窗口的客户区画面，定义帧数据、会话、取消、恢复和失败契约。

### Modified Capabilities

- `window-word-overlay`: 手动识别不再因遮挡拒绝执行；失败提示区分目标不可用与 WGC 捕获失败，浮层仅在合并遮挡比例超过 30% 时整体隐藏。
- `target-window-tracking`: 明确独立的 `IsCapturable` 与 `IsRenderable`，按客户区内遮挡矩形并集判定严格超过 30% 的遮挡，保留事件驱动跟踪、几何重投影和辅助 HWND 登记。
- `recognition-region`: 从 WGC 窗口帧提取客户区及保存的选区，保持区域外文字排除和结果坐标还原。
- `auto-recognition`: 像素变化检测使用 WGC，遮挡时继续检测，目标隐藏或最小化时暂停；基线来自已完成的 `auto-text-change-detection/specs/auto-recognition/spec.md`，该能力尚未同步到主规范，必须先同步旧基线，再合并本次修改。

## Impact

- Windows 平台：新增 WGC/D3D11 适配与必要的互操作绑定，替换 `GdiWindowFrameCapture`；修改自动识别的采样与捕获生命周期衔接。
- Core：调整目标状态判定、补充帧捕获的停止契约和必要错误码；不向 Core 引入 WinRT/D3D 类型。
- App：更新依赖注册、捕获会话停止入口及 `.resx` 提示；保留当前窗口列表和自动识别设置。
- 测试：覆盖遮挡下识别、浮层隐藏、客户区裁剪、像素行间距、取消/超时、尺寸切换、会话释放和真实 HWND 验收。
- 保持 `net10.0-windows10.0.19041.0`；不新增服务端、进程注入、截图持久化或捕获方式设置。
- 本次仅生成规划工件；不修改历史归档、主规范、现有已完成变更或应用实现。技术依据见 [可行性调研](../../../docs/research/window-capture-occlusion-feasibility.md)。
