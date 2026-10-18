## Context

实施期间用户明确要求由其完成实际窗口验证。后续不再运行桌面探针；继续完成代码与自动化测试，将坐标校准、资源增长及真实 HWND 验收保留为用户验收项，见 [验证记录](validation.md)。这替代下文迁移步骤中“实测通过后才接入”的执行顺序，不代表实机验收已经通过。

当前 `IWindowFrameCapture` 的唯一实现是 `GdiWindowFrameCapture`，由 `App.ConfigureServices` 注册。`MeikiOcrWordRecognizer` 与 `AutoRecognitionController` 都依赖该端口，接收 CPU 内存中的 BGRA32；OCR、分词和 LLM 不需要知道捕获 API。

窗口列表与 tracker 使用 `GetClientRect`、`ClientToScreen`，因此 `WindowTarget.Bounds` 实际代表客户区的屏幕物理像素矩形。选区相对于客户区，识别器会将区域 OCR 坐标加回区域偏移。WGC 原始窗口帧不能直接冒充当前客户区帧。

`TargetWindowSnapshot.IsCapturable` 目前排除遮挡，`IsRenderable` 又直接复用它。手动识别刷新 tracker 后检查该属性，自动轮询也检查它。仅替换截图实现不会启用遮挡识别。

历史第一阶段将 GDI 定位为兼容路径并预留 WGC；`follow-target-window` 和 `owned-overlay-window-lifecycle` 明确排除后台捕获。本次修改的是截图来源和截图资格，继续保留浮层的 owner、非置顶和遮挡隐藏规则。依据见 [可行性调研](../../../../docs/research/window-capture-occlusion-feasibility.md)。

已完成但未归档的 `auto-text-change-detection` 提供了当前自动识别实现，其 `auto-recognition` 规范尚未进入 `openspec/specs`。本次该能力的 MODIFIED delta 以那份已完成规范为基线；将来合并规范时先同步旧基线，再应用本次 delta，不能反向覆盖。此次规划不修改或归档那个变更。

## Goals / Non-Goals

**Goals:**

- 未最小化的目标被部分或完全遮挡时，手动和自动识别仍只读取目标 HWND 的内容，不抢前台焦点。
- 保持客户区、区域裁剪、BGRA、OCR 坐标和已保存归一化选区的语义。
- 正确分离捕获资格与 UI 显示资格；后台产生的新识别会话在遮挡期间保留，解除遮挡后显示最新结果。
- 可复用、可取消且能停止的 WGC 会话，正确处理首帧等待、窗口销毁、尺寸变化、设备故障和退出。
- 继续支持当前 Windows 19041 目标；用真实 HWND 验证核心假设，不能以 mock 测试代替遮挡捕获验收。

**Non-Goals:**

- 最小化、显式隐藏、锁屏、安全桌面、独占全屏或受保护内容的通用捕获保证。
- 强制游戏或浏览器在后台持续渲染；注入目标进程或修改其启动参数。
- 取消系统捕获边框、增加系统 picker、捕获方式切换 UI、GDI/PrintWindow/Desktop Duplication 回退。
- 捕获独立顶层菜单、工具提示和其他窗口并合成；只捕获选定 HWND。
- HDR 专用色彩管线、视频编码、录屏文件、跨进程像素流或更换 OCR 模型。

## Decisions

### 1. WGC is the single capture backend

新增 `WgcWindowFrameCapture`，以现有选中的 HWND 调用 `IGraphicsCaptureItemInterop.CreateForWindow`，保留窗口枚举入口。初始化前检查 `GraphicsCaptureSession.IsSupported()`，初始化或捕获失败返回明确错误；删除 GDI 实现和运行时注册，不做静默回退。

理由：只有一种来源才能使“遮挡不污染识别”成为稳定契约。GDI 回退在目标被挡住时重新引入原问题；`PrintWindow` 的目标配合与同步阻塞风险不适合作为本次备用路径。

保持现有 TFM。WinRT 使用 Windows SDK 投影，D3D11 资源操作选用局限于 Platform.Windows 的 `Vortice.Direct3D11` 绑定及其必要 DXGI 依赖；小范围 HWND/WinRT/DXGI 桥接集中在捕获模块。实际发布包版本在第一项技术验证中确认并固定，不为此引入 UI 或视频框架，不向 Core 暴露 native 类型。该绑定项目提供 D3D11 和现代 .NET 支持，参见 [项目源码](https://github.com/amerkoleci/Vortice.Windows)。

实施固定为 `Vortice.Direct3D11` 3.8.3；传递依赖与 MIT 许可记录于 `THIRD-PARTY-NOTICES` 和验证记录。

默认保留系统捕获指示边框，设置 `IsCursorCaptureEnabled = false`；不调用高于当前 OS 基线的去边框 API。不会为开始捕获自动恢复、激活或置顶目标。

### 2. Separate capture eligibility from render eligibility

`IsCapturable` 表示有效、可见且未最小化；`IsRenderable` 在同样条件上额外要求 `!IsOccluded`。`IsForeground` 均不作为必要条件。客户区有效性和 WGC 可用性仍在实际捕获入口校验，状态属性不保证所有 renderer 均受支持。

手动识别和自动采样使用 `IsCapturable`，overlay、popup 和区域选择使用 `IsRenderable`。按用户最新决策，tracker 的 `IsOccluded` 仅在遮挡并集面积严格超过完整客户区面积的 30% 时为 true，恰好 30% 仍显示。分母是完整客户区，不是 OCR 选区；遮挡矩形先裁到客户区，再按 X 边界扫描并合并每条带内的 Y 区间，避免多个窗口重复累计同一面积。保持现有辅助 HWND 排除、owner/Z 顺序和事件合并，不增加设置项或常驻轮询。遮挡事件仅影响 UI，不关闭 WGC、不清空识别结果，也不直接触发 OCR；开启自动识别时，新的目标像素仍按原检测规则触发 OCR。

实际调试发现 ChatGPT 的分层、点击穿透辅助 HWND 被外框矩形误判为实心遮挡。tracker 因此额外排除同时设置 `WS_EX_LAYERED` 与 `WS_EX_TRANSPARENT` 的辅助表面，不依赖进程名或窗口类白名单。普通窗口、单独使用分层或工具窗口样式的窗口仍参与遮挡面积计算。该分类不尝试逐像素测量任意透明窗口的 alpha。30% 阈值取代之前“任何正面积相交就隐藏”的规则，也覆盖自动隐藏任务栏细边缘的情形，因此移除刚加入的 2 物理像素任务栏特判及 AppBar 原生桥接。

不在 `WpfOverlayRenderer.Hide()` 中停止捕获，因为该方法也用于正常刷新和临时隐藏。新结果即使在遮挡期间生成，也替换保存的会话；解除遮挡仅重投影并显示该最新会话。

tracker 的重复快照、前台或 Z 顺序通知，在客户区几何与 DPI 均未改变且浮层已显示时，只维护目标相对层级，不重建画布、不停止 hover。位置/尺寸/DPI 变化、隐藏后恢复和新识别结果仍走完整渲染，避免无变化事件反复关闭并重新打开释义弹窗。

为避免后台识别已经排队的 UI 调用在 Hide 后重新显示，`IOverlayRenderer.Show` 接收平台无关的发布资格检查，WPF 在 Dispatcher 内真正渲染前重新检查识别代次及取消状态；不持有工作线程锁等待 UI。

### 3. Keep frame and crop contracts independent of WGC

`CaptureAsync(target, cancellationToken, region)` 的输出仍是紧密逐行排列的 BGRA32 `CapturedFrame`：无区域时恰好是客户区，有区域时恰好是传入的已解析客户区相对矩形。为帧增加平台无关的 `CaptureGeneration` 元数据，现有构造调用可使用默认值；WGC adapter 在会话创建、失效或几何重建时递增代次，自动检测据此拒绝跨代比较。该元数据不改变像素布局或 OCR 输入。更新端口和 `CropRegion` 中关于“直接屏幕拷贝”的过时说明，不把 WGC native 类型写入领域契约。继续使用现有 `CropRegion.Resolve` 和识别器偏移还原，不另建区域最小尺寸策略。

WGC 帧使用 BGRA8，依据 `ContentSize` 排除未定义部分。先校准原始帧相对于窗口可见边界和客户区的原点，再组合客户区偏移与区域偏移；不能固定减一个标题栏高度或假定 `GetWindowRect` 永远与纹理边缘相同。用 DWM 可见边界、客户区屏幕原点、item/frame 尺寸交叉校验，在有边框、最大化和无边框窗口及不同 DPI 下确认映射。

窗口 style/ex-style（包含最大化状态）或 DPI 改变时重建整个 item/session，避免持续会话保留旧的非客户区原点；单次请求仅允许一次这样的重建，沿用原有 2 秒等待预算。此处理依据首次探针观察及 [WGC 样式切换问题记录](https://github.com/robmikh/Win32CaptureSample/issues/26)，修正后的实际效果由用户验收。

只把需要的客户区/区域复制到 CPU 可读 staging texture，按 `RowPitch` 逐行复制成 `width * height * 4` 个字节，及时 unmap、释放 frame 和 COM 引用。不允许返回 frame pool 借用内存。读取前后的客户区尺寸或帧代次不一致时丢弃该样本，在有限等待内重试；不能将旧尺寸帧交给 OCR 并声明为新尺寸。

### 4. Make capture session ownership explicit

同一时刻只维护当前目标的一份捕获会话。第一次 `CaptureAsync` 惰性创建，后续手动识别和自动采样复用 item、frame pool、session 与设备。端口增加幂等 `Stop()`，用于释放当前会话而不永久销毁 adapter；adapter 自身实现 `IDisposable`，由 DI 在退出时清理。

`MainWindowViewModel` 仅依赖 Core 捕获端口，将显式 Hide、目标切换/清除以及识别失败接到 `Stop()`；关闭自动识别仅停止轮询，保留当前识别显示会话及可复用捕获会话，直到用户结束该会话或目标不可用。adapter 订阅 tracker，在最小化、显式隐藏、销毁及目标改变时停止会话；遮挡和单纯失焦不停止。恢复后由下一次合法采样惰性启动，不由 tracker 事件调用 OCR。退出前停止 controller，再注销捕获回调并释放资源，最后关闭日志。

每次创建/停止会话递增内部代次，并关联目标身份和当前尺寸。停止立即使旧回调、等待者及延迟读回结果失效；同一数值的 HWND 被销毁重建也不能复用旧代次。`Stop()` 可重复调用且不能导致 UI 等待帧到达。与请求并发时先失效代次，再在串行 native 操作完成后释放资源，避免释放仍在 map/copy 的纹理。

退出时先释放已解析的 ViewModel 以取消手动识别并使发布失效，再停止 controller、释放 capture 和容器。UniDic tagger 的释放与已有分词操作共享同一把锁，防止 WGC 工作线程正在解析时释放 native 字典资源。

暂不创建通用多窗口捕获管理器或后端策略系统；一个 adapter 加私有 session/interop/geometry helper 即可。备选“每次采样新建 WGC”会反复初始化且导致系统边框闪烁；备选“永久捕获所有选过的窗口”则不符合当前单目标范围。

### 5. Bound frame waiting and keep native work off the UI thread

使用 `Direct3D11CaptureFramePool.CreateFreeThreaded`，以 2 个 pool buffer 接收画面。回调只获取 frame、更新一份拥有独立生命周期的最新纹理和元数据、释放借用帧，并唤醒等待者；不在回调执行 OCR/分词/LLM，不累积历史队列。D3D11 immediate context 的 copy/map/recreate/dispose 使用同一串行边界，不能与 UI Dispatcher 形成同步等待循环。

新会话、尺寸重建或恢复后的首个有效帧最多等待 2 秒，并响应调用方取消和会话停止；已有当前代次、尺寸匹配的静态画面可以复用。不能仅凭“超过 2 秒没有新 FrameArrived”就认定静态窗口失效。native 帧序号、系统时间和纹理细节留在 adapter 内用于去重与诊断；Core 仅接收用于兼容性比较的捕获代次，不接收 native texture 或录屏时间轴。

自动识别继续使用默认 300ms、最小 100ms 的设置以及现有像素差异和文本确认流程。帧到达频率不决定 OCR 频率；GPU 到 CPU 的读回按请求执行，避免每个回调复制整窗到托管内存。同一轮差异采样和之后 OCR 可以读取同一会话的最新有效帧，不新增保证二者必须共享同一个 WGC frame 的接口重构。

仅遮挡时保留差异比较基线，避免停止后台变化检测。目标改变、捕获重建、尺寸/区域变化或不可用期间使基线失效；恢复后的第一份有效样本建立新基线，后续变化再触发 OCR。该策略不承诺补识别最小化/隐藏期间已经结束的文本变化。单纯恢复或重新投影事件不能启动 OCR/LLM。

### 6. Distinguish unsupported capture, failed capture, and cancellation

继续用 `TargetWindowUnavailable` 表达无效/隐藏/最小化/无客户区；移除其提示中必须“无遮挡”的条件。增加 `WindowCaptureNotSupported`、`WindowCaptureFailed`、`WindowCaptureTimedOut` 等稳定错误码和对应 `.resx` 文案，保留底层英文原因及异常链。调用方取消使用 `OperationCanceledException`，不显示为用户操作失败。

手动失败沿现有 ViewModel 错误入口呈现。自动捕获失败清理本轮等待及比较基线、保留可重试状态，按现有间隔有限重试并记录英文错误；同一连续失败不重复弹窗。对明确不可恢复的当前 item 关闭会话，后续重试重新检查目标和支持状态，避免内部无界重试。自动路径沿用日志反馈，不在本次新增通知系统。

不能通过“全黑”“像素长期相同”启发式认定禁止捕获或程序停止渲染：这些也可能是合法画面。有明确 API 失败时报告失败，否则只对真实取得的像素执行现有规则；不伪造内容，也不强制目标恢复或渲染。

## Risks / Trade-offs

- [WGC 的窗口帧原点随窗口样式变化] → 将客户区裁剪作为第一项真实 HWND 验证，覆盖标题栏、最大化、无边框、负屏幕坐标和 100%/125%/150% DPI；未通过不得只靠比例缩放掩盖偏移。
- [目标程序在后台停止产生画面] → 区分隔离遮挡物和源内容更新，只承诺捕获目标提供的画面；真实游戏验证记录后台是否继续渲染，不注入或修改目标设置。
- [GPU 读回或 COM 生命周期影响性能/稳定性] → 只保留最新纹理，按采样请求读回、遵循 RowPitch；用重复启动/停止、取消和切换的资源测试发现泄漏。
- [旧回调与新目标/新尺寸混用] → 目标身份、会话代次、几何校验和取消共同过滤旧结果；测试销毁后 HWND 复用、晚到回调和在途 Stop。
- [系统边框改变产品观感] → 本次接受正常指示边框，技术验证确认实际显示效果；不暗中调用未经支持的去边框 API。
- [已有自动识别规范未同步] → 保留原变更，先同步其 `auto-recognition` 基线再应用本次 MODIFIED delta；在任务和归档检查中明确顺序。
- [真实桌面测试无法在 CI 中可靠运行] → 自动测试覆盖纯裁剪/状态/会话边界；真实 WGC 验收单独记录系统、GPU、窗口模式与证据，不将未执行的手工检查标为通过。

## Migration Plan

1. 先在现有 TFM 做可丢弃的最小技术验证：确认绑定包可用，捕获一个持续变化的普通窗口和实际游戏，验证完全遮挡与客户区偏移。记录观察和包版本，再进入正式接入。
2. 完成状态分离、WGC adapter、停止契约、单元测试和入口衔接后切换 DI，删除 GDI 实现；更新资源文案和旧接口注释。
3. 运行定向测试、全解决方案 build/test，以及真实 HWND 验收矩阵。现有设置、归一化区域、OCR 模型和 LLM 协议不迁移。
4. 合并规范前先同步已完成自动识别基线，再应用本次 delta。历史 archive 保持不变；不要直接覆盖或重新归档其他变更。
5. 若必须回滚，整体回滚实现和状态/提示变更，恢复旧 GDI 下的“遮挡时拒绝识别”契约；不能只切回 GDI 而保留放宽后的截图资格。

## Open Questions

没有需要用户补充才能开始实施的产品决策。尚待第一项技术验证确认的是实际游戏后台渲染行为、各窗口样式的帧原点、绑定包版本及 GPU/系统兼容性；这些是明确的实施验收项，当前没有实测通过的结论。若实际目标无法提供隔离且正确裁剪的画面，应记录阻碍并调整设计，不能以桌面截图代替验收。
