# window-frame-capture Specification

## Purpose

Capture the selected window client area with Windows.Graphics.Capture independently of desktop occlusion, with owned frames and bounded session lifecycles.

## Requirements

### Requirement: Capture selected-window content independently of desktop occlusion

系统 SHALL 使用 Windows.Graphics.Capture 按选中的 HWND 捕获该窗口自身的画面。有效、可见、未最小化且具有有效客户区的目标 SHALL 不因失去前台焦点、部分遮挡或完全遮挡而被拒绝。捕获 SHALL NOT 混入其他顶层窗口、KotobaSenpai 浮层或屏幕光标；系统 SHALL NOT 为捕获激活、恢复或置顶目标。系统 SHALL 使用当前 OS 基线支持的 API，并保留正常的系统捕获指示边框。

#### Scenario: Capture a partially covered target
- **WHEN** 其他窗口覆盖目标客户区的一部分且 WGC 可以捕获目标
- **THEN** 返回的帧只包含目标自身对应区域，不包含遮挡窗口的文字或图像

#### Scenario: Capture a fully covered inactive target
- **WHEN** 目标仍可见且未最小化，但完全被其他窗口覆盖并失去前台焦点
- **THEN** 系统仍允许从该 HWND 获取画面，不抢焦点；若目标继续产生新画面，新画面能够进入后续采样

#### Scenario: Avoid capturing the recognition overlay
- **WHEN** 目标上方显示本应用的下划线、振假名、释义弹窗或区域选择窗口
- **THEN** WGC 帧不包含这些独立顶层辅助窗口，识别不会递归读取自己的标记

#### Scenario: A source application stops rendering in the background
- **WHEN** 目标在后台没有产生新画面但已有一份有效 WGC 帧
- **THEN** 系统只处理实际提供的画面，不将静止内容伪装为新内容，也不干预目标程序的渲染设置

### Requirement: Return owned client-area BGRA frames

`IWindowFrameCapture` SHALL 返回由调用方安全持有的 CPU BGRA32 数据，逐行紧密排列为 `width * height * 4` 字节。无区域参数时返回完整客户区，指定已解析区域时只返回该客户区相对矩形；标题栏、窗口边框及帧纹理中超出有效内容的部分 SHALL NOT 进入 OCR 输入。D3D 行间距 SHALL 按实际 `RowPitch` 处理，返回内存 SHALL NOT 借用 frame pool 缓冲区。帧 SHALL 携带平台无关的 `CaptureGeneration` 元数据，在会话或几何重建后改变，以使自动检测能够拒绝跨代比较；该元数据 SHALL 不改变像素布局。

#### Scenario: Copy a texture with padded rows
- **WHEN** staging texture 的 `RowPitch` 大于有效像素行宽
- **THEN** 输出逐行排除 padding，像素顺序和帧宽高正确，不产生行错位

#### Scenario: Read the full client area
- **WHEN** 调用方未提供识别区域且取得有效帧
- **THEN** 输出尺寸与该次捕获所依据的客户区一致，左上角对应客户区原点而不是标题栏原点

#### Scenario: Keep returned memory valid
- **WHEN** WGC 借用帧被释放、后续帧到达或捕获会话停止
- **THEN** 先前成功返回的 `CapturedFrame` 像素仍然有效且不会被后续采样改写

#### Scenario: Identify a rebuilt capture session
- **WHEN** 原会话失效后新会话返回一份与旧帧尺寸相同的画面
- **THEN** 新帧的 `CaptureGeneration` 与旧会话不同，自动检测可以建立新基线而不误用旧代次样本

### Requirement: Keep capture geometry consistent

系统 SHALL 将 WGC 帧原点、有效内容尺寸和目标客户区物理像素几何对应后再提取像素，并支持有边框、最大化和无边框窗口。尺寸或帧代次与客户区不一致时，系统 SHALL 丢弃样本并在本次有限等待内重新获取，SHALL NOT 用旧帧伪装新尺寸。窗口大小改变后系统 SHALL 重建受影响的 pool/texture；纯屏幕位置变化 SHALL NOT 被当作 OCR 区域内的像素变化。

#### Scenario: Resize while a frame is being acquired
- **WHEN** 旧尺寸帧到达时目标客户区已经改变大小
- **THEN** 系统不把该帧作为新客户区帧返回，重建必要资源并等待几何一致的帧或返回有限等待失败

#### Scenario: Move across displays with different DPI
- **WHEN** 目标移动到不同 DPI 的显示器，包含负桌面坐标位置
- **THEN** 系统重新校验客户区裁剪，返回的物理像素与区域坐标保持一致，不使用固定标题栏高度补偿

### Requirement: Reuse and stop the selected capture session

系统 SHALL 惰性创建并复用当前目标的一份 WGC 会话，而不是为每次自动采样新建会话。捕获端口 SHALL 提供可重复调用的 `Stop()`；显式结束识别显示、目标切换/清除、目标隐藏/最小化/销毁以及应用退出 SHALL 使旧会话和在途请求失效，并释放回调、frame pool、session 和纹理。单纯遮挡、失焦或 renderer 临时隐藏 SHALL NOT 停止捕获。重新合法采样 SHALL 能创建新的会话代次。

#### Scenario: Reuse capture during automatic polling
- **WHEN** 当前目标持续可捕获且多次轮询请求帧
- **THEN** 系统复用当前会话，不为每个采样间隔重新启动 WGC

#### Scenario: Stop during an outstanding request
- **WHEN** 用户结束识别显示或切换目标时旧捕获请求仍在等待
- **THEN** 等待以取消结束，旧回调和晚到结果不能满足新目标请求；资源在 native 操作安全结束后释放，UI 不等待新帧到达

#### Scenario: Hide or minimize the target
- **WHEN** 目标被显式隐藏或最小化
- **THEN** 系统停止旧捕获会话，不返回该不可用期间的旧缓存；恢复后下一次合法采样等待新代次的有效帧

#### Scenario: Reuse a numeric HWND after destruction
- **WHEN** 原目标销毁后新窗口使用了相同数值的 HWND
- **THEN** 系统使用新目标/会话代次，不向新窗口请求返回原目标缓存的像素

#### Scenario: Exit with active capture
- **WHEN** 应用退出且捕获或自动采样仍在运行
- **THEN** 系统先停止自动采样，再取消并释放捕获资源，完成后关闭日志，不留下仍持有旧会话的回调

### Requirement: Bound asynchronous frame acquisition

系统 SHALL 在非 UI 线程执行等待帧和 GPU 读回。新会话或重建后首份几何有效的帧 SHALL 最多等待 2 秒，并响应调用方取消及会话停止。已有当前代次且几何匹配的静态帧 SHALL 可复用，不因未收到新事件而超时。接收回调 SHALL 只保留最新的拥有独立生命周期的画面，不建立随帧数增长的待处理队列，也 SHALL NOT 直接运行 OCR 或 LLM。

#### Scenario: No initial frame arrives
- **WHEN** 会话启动后 2 秒内没有提供有效帧且请求未被取消
- **THEN** 系统返回可重试的捕获超时，UI 保持可操作，不回退为桌面截图

#### Scenario: Cancel before frame arrival
- **WHEN** 调用方在等待期间取消请求
- **THEN** 操作以 `OperationCanceledException` 结束，不作为识别失败提示，也不遗留继续等待的请求

#### Scenario: Read a static window again
- **WHEN** 当前会话已有几何匹配的有效帧，但静态窗口没有新的 `FrameArrived`
- **THEN** 后续采样可返回该有效画面，不强制等待不存在的新动画帧，也不单凭帧年龄报告失败

#### Scenario: Receive frames faster than OCR polling
- **WHEN** WGC 到帧速度高于当前识别采样间隔
- **THEN** 系统只保留最新画面，GPU 到 CPU 读回按采样请求进行，OCR 和 LLM 不随每个到帧事件运行

### Requirement: Report capture failures without desktop fallback

系统 SHALL 区分目标不可用、WGC 不受支持、捕获失败和首帧超时，提供稳定错误码、本地化可操作提示及英文诊断。失败 SHALL NOT 静默调用 GDI、PrintWindow 或显示器捕获替代选中窗口；系统 SHALL NOT 根据全黑或长期相同的像素擅自认定受保护内容。受保护或排除捕获的内容 SHALL NOT 被承诺可读取。

#### Scenario: WGC is unavailable or session creation fails
- **WHEN** 支持检查失败或不能为选中 HWND 创建捕获会话
- **THEN** 系统返回相应不支持或捕获失败状态，允许重试或重新选择，不识别桌面上同一矩形里的内容

#### Scenario: A recoverable session failure occurs
- **WHEN** 捕获过程中发生 pool、item 或设备错误
- **THEN** 系统使旧帧失效并清理失效资源；后续合法请求可以重新初始化，当前请求不会无限内部重试

#### Scenario: A valid frame is black
- **WHEN** API 正常返回一份全黑且几何有效的帧
- **THEN** 系统按实际像素处理，不伪造文本，也不把颜色特征当作确定的禁止捕获错误
