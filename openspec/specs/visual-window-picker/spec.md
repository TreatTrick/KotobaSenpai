# visual-window-picker Specification

## Purpose
Provide explicit desktop window selection with a flowing rainbow outline, a static preview, and screenshot flight feedback while keeping selection independent of recognition.

## Requirements

### Requirement: Enter an explicit desktop picking session

系统 SHALL 通过识别页的选择按钮进入桌面拾取模式，暂时隐藏主窗口并显示点击确认和 Esc 取消提示。拾取期间 SHALL 保留当前目标及静态缩略图，不因悬停候选改变目标、切换跟踪或启动候选捕获。选择命令 SHALL 防止重入，拾取和预览获取期间 SHALL 阻止用户启动识别或区域编辑。拾取完成、取消或失败后 SHALL 恢复主窗口原来的位置及正常 / 最大化状态。

#### Scenario: Start picking with an existing selection
- **WHEN** 用户已有目标和缩略图并点击重新选择窗口
- **THEN** 主窗口暂时隐藏并进入拾取模式，原目标、图片及未结束的识别会话保持有效，候选悬停不触发 Attach、WGC、OCR 或 LLM

#### Scenario: Cancel picking
- **WHEN** 用户在确认前按 Esc，且原目标仍然有效
- **THEN** 系统结束拾取并恢复主窗口，保留原目标和原缩略图，不因取消而终止原识别会话

#### Scenario: Reject overlapping operations
- **WHEN** 一次拾取或预览获取尚未完成时再次触发选择、识别或区域编辑命令
- **THEN** 系统不创建并行的拾取、预览、识别或区域编辑操作

#### Scenario: Fail to initialize the picker
- **WHEN** 输入 hook 或拾取辅助窗口无法初始化
- **THEN** 系统清理已经创建的资源，恢复主窗口及仍然有效的原选择，显示可重试的本地化提示并记录英文诊断

### Requirement: Resolve eligible top-level window candidates

系统 SHALL 按鼠标当前位置和实际窗口层级解析可选择的顶层窗口，子控件 SHALL 解析到所属 root window，独立顶层对话框 SHALL 保持为对话框本身。系统 SHALL 排除本应用窗口及辅助 HWND、桌面、任务栏、不可见、cloaked、最小化、已销毁和无有效客户区的窗口。排除本应用辅助表面后 SHALL 继续定位真实候选；其他应用的正常遮挡窗口 SHALL NOT 被穿透。无标题的有效窗口 SHALL 使用本地化名称占位。确认时 SHALL 重新校验候选身份和客户区几何。

#### Scenario: Point at a child control
- **WHEN** 鼠标位于应用窗口内的按钮或文本控件上
- **THEN** 候选和最终目标为该控件所属的有效顶层窗口，而不是子控件 HWND

#### Scenario: Point at an owned dialog
- **WHEN** 鼠标位于其他应用的有效顶层对话框上
- **THEN** 系统选择对话框本身，不自动替换成它的 owner

#### Scenario: Ignore the picker surface
- **WHEN** 本应用的彩虹边框、提示或已有辅助浮层位于鼠标位置
- **THEN** 系统跳过这些表面，仍能定位其下符合条件的真实窗口，不将辅助层选为目标

#### Scenario: Respect another application's occluding window
- **WHEN** 一个有效普通窗口遮住另一个窗口的鼠标所在位置
- **THEN** 系统将前面的有效窗口作为候选，不选择后面的窗口

#### Scenario: Point at an excluded surface
- **WHEN** 鼠标位于桌面、任务栏或其他不满足选择资格的位置
- **THEN** 系统没有可确认候选，用户可以继续移动鼠标或按 Esc 取消

#### Scenario: Point at an untitled window
- **WHEN** 鼠标位于没有标题但其他条件均有效的普通顶层窗口
- **THEN** 系统允许选择该窗口，并使用本地化名称占位

### Requirement: Show a flowing rainbow outline during picking

拾取模式下，系统 SHALL 沿当前候选窗口的可见外轮廓显示彩虹边框，包含标题栏，并在窗口移动或缩放时更新位置。启用系统动画时颜色 SHALL 沿边框周长连续流动，边框和候选窗口自身 SHALL NOT 旋转。边框 SHALL 不激活窗口、不接收鼠标点击、不出现在任务栏；其临时置顶行为 SHALL 仅用于拾取及待识别提示层。辅助 HWND SHALL 登记和注销，不改变普通识别浮层的 owner / Z 顺序或遮挡规则。无候选时 SHALL 隐藏拾取边框，拾取结束时 SHALL 结束候选刷新；确认后 SHALL 按待识别规则继续显示已选目标的边框。

#### Scenario: Move from one candidate to another
- **WHEN** 用户将鼠标从一个有效窗口移动到另一个有效窗口
- **THEN** 前一个窗口的边框移除，新的窗口显示沿自身外轮廓流动的彩虹边框

#### Scenario: Keep the outline aligned
- **WHEN** 当前候选在拾取期间移动、缩放或使用不同 DPI
- **THEN** 边框重新对齐可见外框，客户区捕获坐标不被外框坐标替换

#### Scenario: Stop the picker animation
- **WHEN** 用户确认、取消或拾取发生异常
- **THEN** 拾取提示消失，候选刷新停止，普通覆盖层维持既有层级规则；确认后保留新目标的待识别边框，取消或失败后恢复仍有效的原待识别边框

### Requirement: Keep the selected window highlighted until recognition starts

系统 SHALL 在确认目标后继续沿该窗口的可见外框显示彩虹流动边框，并跟随移动、缩放和 DPI 变化，直到用户通过识别入口开始识别。待识别边框 SHALL 保持原生点击穿透且不可激活，不显示拾取提示，不安装输入 hook，不保留预览采样会话；预览捕获、重试或飞入结束 SHALL NOT 使其消失。系统关闭动画时 SHALL 保留静态彩虹边框。识别开始前、目标关闭、选择清空或应用退出时 SHALL 关闭对应边框并清理计时器及登记。识别失败或结束后 SHALL NOT 自动恢复该提示，重新确认选择后 SHALL 重新显示。

#### Scenario: Retain the outline after preview or flight
- **WHEN** 目标已确认，预览和飞入已经完成或预览获取失败，用户尚未开始识别
- **THEN** 已选目标继续显示彩虹边框，显式重试预览也不结束边框，之后的普通输入正常传给目标

#### Scenario: Remove the outline before recognition
- **WHEN** 用户对已选目标点击开始识别
- **THEN** 系统在调用识别工作流前关闭待识别边框及其刷新，识别尚未返回或随后失败、结束时不自动恢复该边框

#### Scenario: Cancel reselection before or after recognition
- **WHEN** 用户重新进入拾取并取消或拾取失败，原目标仍然有效
- **THEN** 原目标本次选择尚未开始识别时恢复其边框，已经开始过识别时不恢复；成功重新选择同一窗口时重新显示待识别边框

#### Scenario: Hide and restore a pending target
- **WHEN** 待识别目标最小化或隐藏后恢复可见
- **THEN** 边框暂时隐藏并在恢复后重新贴合目标，静态缩略图不重新获取

#### Scenario: Release a pending outline
- **WHEN** 目标关闭、其句柄身份变化、选择清空或应用退出
- **THEN** 系统关闭对应边框，停止刷新并注销辅助 HWND，不将边框转移到复用句柄的新窗口

### Requirement: Consume the selection gesture without operating the target

系统 SHALL 在拾取模式内接住左键按下及匹配的释放，防止确认点击同时操作目标应用。按下和释放 SHALL 对应同一个仍然有效的候选才允许提交；窗口身份变化或释放到其他候选 SHALL 不提交并继续拾取。无候选处的左键点击 SHALL 同样被消费。Esc SHALL 成对消费按下及释放后取消拾取，避免作为取消按键传给目标。输入回调 SHALL 不等待截图、绘制或识别；确认、取消、失败和应用退出 SHALL 释放输入 hook，不拦截之后的正常输入。

#### Scenario: Confirm over an interactive button
- **WHEN** 用户在候选应用的按钮上按下并释放左键
- **THEN** 系统只提交该窗口，目标按钮不收到此次按下和释放，之后的普通点击正常工作

#### Scenario: Release over a different candidate
- **WHEN** 左键按下后鼠标移到另一候选再释放
- **THEN** 两个输入事件均不透传，不提交窗口，并保持拾取状态

#### Scenario: Lose the candidate before release
- **WHEN** 按下后候选销毁、最小化或其 HWND 在确认前被其他窗口身份复用
- **THEN** 系统不提交原候选或被复用的新窗口，不返回失效目标，并继续允许用户选择或取消

#### Scenario: Click an invalid location and cancel
- **WHEN** 用户在没有有效候选的位置单击，随后按 Esc
- **THEN** 无效点击不操作底层界面，Esc 退出拾取，系统恢复主窗口并清理输入 hook

### Requirement: Commit a selection independently of recognition

系统 SHALL 在确认手势完成并校验成功后提交新目标，此时才结束旧识别、停止旧自动采样、使旧结果失效并切换目标跟踪。重新选择同一窗口 SHALL 同样构成一次新选择。选窗和获取预览 SHALL NOT 自动调用 OCR、LLM 或启动自动识别；用户 SHALL 通过现有识别入口开始识别。

#### Scenario: Commit a different target
- **WHEN** 用户确认一个有效的新窗口
- **THEN** 系统结束旧目标会话、清除旧缩略图、跟踪新目标并获取其预览，保持新目标为待识别状态

#### Scenario: Select the same window again
- **WHEN** 用户明确重新拾取当前已选窗口
- **THEN** 系统结束已有识别会话并获取一张新的静态预览，不因 HWND 相同而跳过提交逻辑

### Requirement: Acquire one static preview with a bounded capture lifecycle

每次成功提交选择后，系统 SHALL 使用现有 WGC 捕获端口请求完整客户区的一帧，不应用 OCR 选区，不包含标题栏、其他窗口或自身辅助层。获取 SHALL 遵守现有首帧最多 2 秒、取消和几何一致性约束。仅用于预览的捕获会话 SHALL 在本次成功、失败或取消后停止，并在允许启动新识别前完成清理所有权交接。系统 SHALL NOT 为静态预览保持采样、定时截图或建立实时缩略图连接。

#### Scenario: Capture a target with an existing recognition region
- **WHEN** 用户选中一个窗口且已经保存了识别子区域
- **THEN** 缩略图显示完整客户区，识别子区域设置保持不变，不被应用到缩略图捕获

#### Scenario: Finish a preview-only request
- **WHEN** 预览获取成功、失败或被取消
- **THEN** 本次预览所用 WGC 会话停止，后续新识别不会被旧预览的清理回调停止

#### Scenario: The source changes after selection
- **WHEN** 静态预览已经显示且目标画面改变、识别执行、窗口移动 / 缩放或页面重新显示
- **THEN** 系统保持原静态图，不为这些事件重新截图或更新为实时画面

### Requirement: Animate the captured image into the recognition page

预览成功时，系统 SHALL 使用该帧的图片副本，从捕获时对应的客户区屏幕位置缩小并沿轻微弧线飞入已恢复主窗口的预览区域，完成后给予轻微落入反馈并显示来自同一帧的静态缩略图。正常动画时长 SHALL 约为 400 毫秒。系统 SHALL 保持真实目标窗口的位置和大小不变，飞行呈现 SHALL 原生点击穿透且不可激活。页面布局有效后 SHALL 测量实际图片终点，并在需要时将卡片滚动到可见位置。

#### Scenario: Complete the image flight
- **WHEN** 本次选择取得有效帧且识别页预览区域可见
- **THEN** 该帧副本从源客户区位置飞入正方形预览区域，结束后同帧缩略图保持显示，目标应用窗口不被移动或缩放

#### Scenario: Reach a preview outside the previous scroll position
- **WHEN** 主窗口恢复时目标卡片在滚动区域之外
- **THEN** 系统先将卡片滚动到可见位置并取得有效布局，再计算动画终点

#### Scenario: Interrupt the animation
- **WHEN** 飞行过程中用户重新选择、切换页面、隐藏或关闭主窗口，或者显示器布局改变
- **THEN** 系统停止旧动画并释放临时窗口；仍有效的已提交选择显示最终静态结果，旧动画完成回调不能修改新状态

#### Scenario: Degrade only the presentation
- **WHEN** 主窗口无法激活、有效动画终点不可获得或动画呈现失败
- **THEN** 系统恢复主窗口可见状态并直接显示最终预览，不撤销目标或把动画失败作为捕获失败

### Requirement: Keep geometry correct across monitors and respect reduced motion

系统 SHALL 区分可见窗口外框、客户区截图矩形和预览框内等比例图片矩形，以屏幕物理像素为统一几何基础，并按每个呈现表面的 DPI 转换为 WPF DIP。系统 SHALL 支持多显示器负坐标以及 100%、125%、150% 混合缩放。系统关闭动画时 SHALL 用静态彩虹边框和直接显示最终预览完成选择，保持输入和绑定行为一致。

#### Scenario: Pick on a monitor with a negative origin
- **WHEN** 目标窗口位于主显示器左侧或上侧且屏幕坐标为负
- **THEN** 候选命中、彩虹边框和截图飞行起点保持正确，不把负坐标裁成零

#### Scenario: Fly between monitors with different DPI
- **WHEN** 目标与主窗口分别位于不同缩放比例的显示器
- **THEN** 边框贴合目标可见外轮廓，截图副本跨屏位置和尺寸连续，最终按实际布局落入预览区域

#### Scenario: Respect disabled system animations
- **WHEN** 系统动画被关闭
- **THEN** 系统保留静态彩虹边框、点击选择及缩略图结果，跳过连续彩虹流动、飞行和回弹动画

### Requirement: Report preview failure and target availability explicitly

预览失败 SHALL 保留已经提交的新目标，显示新窗口标题、本地化失败状态和重试入口，SHALL NOT 显示旧目标图片或伪造成功动画。重试 SHALL 只为当前有效目标获取新预览，不启动识别或重播选窗飞行；在有在途识别或活动识别会话时 SHALL 禁用重试，避免停止识别捕获。目标隐藏 / 最小化时 SHALL 标记暂不可用并保留静态图，恢复时 SHALL 不自动更新图片；目标销毁时 SHALL 清除有效选择和图片并提供重新选择。真实生命周期事件 SHALL 优先于取消时保存的旧状态。

#### Scenario: Fail to acquire the initial preview
- **WHEN** 新目标已提交但 WGC 不支持、失败或首帧超时
- **THEN** 卡片显示新目标、预览失败和可用时的重试入口，不保留旧目标图片、不回退桌面截图、不播放虚假的飞入动画

#### Scenario: Retry the current preview
- **WHEN** 用户对仍有效且可捕获的已选目标重试失败的预览
- **THEN** 系统获取其一帧并更新静态预览，结束预览会话，不执行 OCR / LLM 或桌面飞行动画

#### Scenario: Prevent a retry from interrupting recognition
- **WHEN** 用户在预览失败后启动了识别，且识别请求或会话仍在运行
- **THEN** 系统禁用预览重试，不对运行中的捕获调用预览 Stop；现有结束识别或重新选择入口仍然可用

#### Scenario: Minimize and restore the target
- **WHEN** 当前目标最小化或隐藏，随后恢复可见
- **THEN** 卡片先标记暂不可用并保留静态图，恢复后只更新可用状态，不重新获取图片

#### Scenario: Close the original target during picking
- **WHEN** 原目标在用户拾取其他窗口的过程中关闭
- **THEN** 系统清除原目标和图片，允许继续拾取；若随后取消，则恢复主窗口并显示原目标已关闭，不复活旧选择

#### Scenario: Close the committed target during preview
- **WHEN** 已提交的新目标在预览获取或飞行动画期间关闭
- **THEN** 系统清除失效目标和图片、停止对应预览与动画并提供重新选择，晚到帧不复活该目标

### Requirement: Reject stale results and release temporary image resources

选窗、预览和动画 SHALL 具有可核验的选择代次。新选择、目标失效、取消在途预览或应用退出后，旧帧、旧错误及旧呈现完成回调 SHALL NOT 覆盖当前状态。系统 SHALL 仅在内存中保留当前缩略图及短暂动画图像，不持久化或上传，也不把选窗预览送入识别诊断输出。全尺寸帧和飞行图像 SHALL 在呈现完成或取消后释放；替换、目标关闭和退出 SHALL 释放对应图片。

#### Scenario: Receive an old frame after the target changes
- **WHEN** 新的选择代次已经生效后旧预览帧或旧错误才返回
- **THEN** 系统丢弃该结果并释放其资源，不覆盖当前标题、缩略图、状态或捕获会话

#### Scenario: Exit during picking or flight
- **WHEN** 应用在拾取、预览获取或飞行动画期间退出
- **THEN** 系统取消在途工作、拆除输入 hook、关闭临时窗口并清理捕获与图像资源，不向已关闭视图发布结果

#### Scenario: Keep preview content in memory
- **WHEN** 选择、预览、动画和替换目标正常完成
- **THEN** 仅当前缩略图继续驻留内存，没有新增截图文件、诊断图片或远端图片请求
