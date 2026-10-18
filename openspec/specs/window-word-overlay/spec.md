# window-word-overlay Specification

## Purpose
TBD - created by archiving change phase1-window-word-overlay. Update Purpose after archive.
## Requirements
### Requirement: Use an owned target-relative overlay window
系统 SHALL 将覆盖层创建为透明、点击穿透、不可激活的 Win32 owned top-level WPF 窗口，其 owner SHALL 为当前 Aokana 目标 HWND，且 SHALL 使用 `Topmost = false`。覆盖层 SHALL 跟随 owner 的生命周期和目标相邻的 Z 顺序，不得浮在遮挡目标的其他窗口之上。目标失去前台焦点但仍可见且客户区合并遮挡比例不超过 30% 时，覆盖层 SHALL 继续允许显示。

#### Scenario: Attach overlay to selected target
- **WHEN** 用户为有效的 Aokana HWND 显示一个识别会话
- **THEN** 系统将 overlay HWND 的 owner 设置为该目标 HWND，关闭全局置顶，并在目标客户区上方以非激活、点击穿透方式显示词线和假名

#### Scenario: Aokana loses foreground focus
- **WHEN** Aokana 仍可见且合并遮挡比例不超过 30%，但 KotobaSenpai 或其他窗口获得焦点
- **THEN** 覆盖层不因目标失去前台状态而清空或隐藏当前会话

#### Scenario: Another window occludes Aokana
- **WHEN** 位于 Aokana 上方的有效遮挡窗口在客户区内的并集面积严格超过完整客户区的 30%
- **THEN** 系统将目标视为遮挡并隐藏整个覆盖层，继续保留当前识别会话

#### Scenario: Small overlap keeps annotations available
- **WHEN** 客户区合并遮挡比例为 30% 或更少，目标仍有效、可见且已恢复
- **THEN** 覆盖层仍允许显示，保持目标相邻的非置顶 Z 顺序，不在遮挡窗口上方绘制目标标记

### Requirement: Window selection
系统 SHALL 枚举当前可见的顶层窗口，并允许用户选择一个具有有效窗口句柄的目标窗口。选择结果 SHALL 只保存在当前进程内，不得写入截图或原始窗口内容。

#### Scenario: Select a visible window
- **WHEN** 用户打开窗口选择列表并选择一个可见顶层窗口
- **THEN** 系统返回该窗口的句柄、标题和屏幕边界，并将其设为当前目标窗口

#### Scenario: Ignore unusable windows
- **WHEN** 枚举过程中窗口不可见、已销毁或没有有效客户区
- **THEN** 系统不把该窗口放入可选列表，并继续枚举其他窗口

### Requirement: OCR words with coordinates
系统 SHALL 对当前目标窗口执行一次日语 OCR，并为每个非空识别字符返回原文、字符框和相对于捕获帧的坐标。字符框坐标 SHALL 使用非负宽高的物理像素矩形。识别字符 SHALL 按阅读顺序排列（第一阶段仅横排，自左向右；竖排属第二阶段）。

#### Scenario: Recognize Japanese words
- **WHEN** 目标窗口捕获成功且系统具备 meikiocr 本地模型
- **THEN** 系统返回按阅读顺序排列的字符列表，每个字符包含非空文本和字符框坐标

#### Scenario: Japanese OCR language is unavailable
- **WHEN** meikiocr 模型文件缺失或推理失败
- **THEN** 系统不生成伪造字符坐标，并返回可操作、指明缺失模型或失败原因的错误

#### Scenario: Empty or invalid OCR result
- **WHEN** OCR 返回空文本、零面积或越界字符框
- **THEN** 系统过滤无效项；若没有剩余字符则返回空识别结果而不是抛出未处理异常

### Requirement: Screen coordinate mapping
系统 SHALL 将捕获帧坐标映射为目标窗口在桌面上的屏幕物理像素坐标，并裁剪到窗口边界内。映射结果 SHALL 在 DPI 缩放为 100%、125% 和 150% 时保持比例正确。

#### Scenario: Map a word to screen coordinates
- **WHEN** 捕获帧大小与目标窗口屏幕矩形已知
- **THEN** 系统将词框按 X/Y 比例平移到屏幕矩形，并返回不超出窗口边界的矩形

#### Scenario: Handle DPI scaling
- **WHEN** 目标窗口使用非 100% DPI
- **THEN** 系统依据实际捕获像素与屏幕物理像素比例转换，而不是直接把 DIP 当成像素

### Requirement: Word underline overlay
系统 SHALL 在目标窗口客户区上方显示透明、点击穿透的覆盖层,并为每个本地分词词的**每个 rect** 在其对应行的词框下方绘制横线(跨行词因此有多条下划线,各贴各自行底);对每个已验证 phrase group,系统 SHALL 为其每个 part 的每个行内几何绘制可识别的组合词块标记。覆盖层 SHALL 使用目标 HWND owner 和非置顶 Z 顺序，目标被其他窗口遮挡时不得显示在遮挡窗口上方。覆盖层刷新时 SHALL 整体替换当前词和 group 列表,隐藏时 SHALL 清除已绘制标记并恢复点击穿透；临时隐藏 SHALL 保留保存的会话数据，显式结束会话 SHALL 清理保存的数据并阻止晚到结果重新发布。悬停一个词的**任一 rect** 时,系统 SHALL 高亮该词的**全部 rect**(整词一起变色);悬停 group 的任一 part 时,系统 SHALL 高亮该 group 的全部 parts。启用短语分析时，覆盖层 MAY 先显示本地振假名而暂不绘制下划线；分析批次完成后的刷新 SHALL 仅为成功句段中的词绘制下划线。关闭短语分析时 SHALL 保持原有本地下划线行为。目标窗口移动、缩放或 DPI 变化时，覆盖层 SHALL 按目标跟踪快照重映射已有归一化几何，跟踪事件本身不得重新 OCR；独立启用的自动识别仍按采样和像素变化规则运行。目标被隐藏、最小化或完整客户区的合并遮挡比例严格超过 30% 时 SHALL 隐藏覆盖层并停止 hover 交互但保留当前会话，目标恢复可见、未最小化且合并遮挡比例不超过 30% 时 SHALL 恢复最新保存的会话；目标失效、销毁或切换时 SHALL 清理当前会话。

#### Scenario: Draw one underline per eligible word
- **WHEN** 当前会话包含一个或多个可下划线的本地分词词
- **THEN** 覆盖层为每个词的每个 rect 各绘制一条与词框宽度对齐的横线(跨行词每行一条),线条位于各自词框底部内侧且至少有 1px 可见线宽

#### Scenario: Draw one underline per word
- **WHEN** 当前会话包含一个或多个本地分词词
- **THEN** 覆盖层为每个词的每个 rect 各绘制一条与词框宽度对齐的横线(跨行词每行一条),线条位于各自词框底部内侧且至少有 1px 可见线宽

#### Scenario: Draw local word and phrase markers
- **WHEN** 当前会话包含本地词和一个或多个 phrase group
- **THEN** 覆盖层绘制本地词横线,并为 group 的所有 part 几何绘制对应标记

#### Scenario: Show furigana while analysis is pending
- **WHEN** 首次或全变识别的短语分析已启用但尚未完成，且目标满足显示资格
- **THEN** 覆盖层显示本地振假名,不绘制下划线或 group 标记

#### Scenario: Restrict lines after partial analysis
- **WHEN** 最终会话同时包含成功句段词和失败句段词
- **THEN** 只有成功句段词绘制下划线,失败句段词仍可显示振假名但没有下划线

#### Scenario: Preserve disabled-analysis behavior
- **WHEN** 短语分析未启用
- **THEN** 本地词和振假名按原有行为同时显示下划线

#### Scenario: Refresh and hide overlay
- **WHEN** 用户重新识别窗口或关闭覆盖层
- **THEN** 旧词线、振假名和 group 标记都不会残留;关闭后覆盖层不可见、不可激活且不拦截鼠标输入

#### Scenario: Follow target movement
- **WHEN** 已有会话的目标窗口移动或客户区改变大小
- **THEN** 覆盖层使用相对于原客户区保存的归一化几何重新定位和缩放所有横线、短语标记与假名，不重新 OCR 且不产生累计缩放误差

#### Scenario: Respect target occlusion
- **WHEN** 目标客户区内有效上层遮挡矩形的并集面积严格超过完整客户区的 30%
- **THEN** 覆盖层隐藏且保留当前会话；可捕获时识别仍可更新保存的结果，遮挡面积计算不重复计数且不使用 OCR 选区作为分母

#### Scenario: Hide temporarily without losing the session
- **WHEN** 目标窗口被隐藏、最小化或客户区合并遮挡比例严格超过 30%，但目标 HWND 仍然有效
- **THEN** 覆盖层立即隐藏、停止 hover、关闭 popup，但保留当前词线、假名、短语标记和归一化会话数据供恢复使用，仍属当前会话的已完成分析可以更新保存的结果

#### Scenario: Restore after target becomes visible
- **WHEN** 目标窗口恢复可见、未最小化且合并遮挡比例回到 30% 或更少
- **THEN** 覆盖层按最新客户区快照恢复显示最新保存会话的几何，恢复动作本身不执行 OCR 或 LLM 分析；独立自动识别仍可按采样规则运行

#### Scenario: Hide on minimize or destruction
- **WHEN** 目标窗口被最小化、销毁或当前目标切换到另一个 HWND
- **THEN** 最小化时覆盖层立即隐藏但保留当前会话；销毁或切换时覆盖层立即隐藏并清理当前会话、hover 状态、popup 和所有绘制元素，允许用户重新选择窗口

#### Scenario: Change line color on hover
- **WHEN** 鼠标悬停在某个词的任一 rect 上
- **THEN** 该词的**全部 rect** 横线由默认色变为悬停色(整词一起高亮),其他词的横线保持默认色

#### Scenario: Restore color on mouse leave
- **WHEN** 鼠标移出悬停词的包围盒
- **THEN** 该词的全部 rect 横线恢复为默认色

#### Scenario: Highlight all parts of a group
- **WHEN** 鼠标悬停 phrase group 的任一 part 时
- **THEN** 该 group 的所有 part 几何同时进入悬停状态,并保持其他 group 的状态不变

#### Scenario: Overlay stays click-through while hovering
- **WHEN** 鼠标悬停在词热区或高亮 phrase group
- **THEN** 覆盖层整体仍不拦截点击,点击事件穿透到下方窗口

### Requirement: Failure reporting
系统 SHALL 将目标窗口不可见、最小化、销毁、捕获不受支持、捕获失败或超时、OCR 不可用、覆盖层创建失败和实时跟踪失败转换为用户可理解的错误状态，并允许用户重新选择窗口或重试当前窗口。有效、可见且已恢复的目标 SHALL 不因遮挡或失去前台焦点而被拒绝 WGC/OCR；目标不可见、最小化、无效或没有有效客户区时显式识别 SHALL 被拒绝。覆盖层显示不要求目标处于前台，但仍要求目标处于可见、已恢复且客户区合并遮挡比例不超过 30% 的状态。窗口状态事件本身 SHALL NOT 自动重新 OCR；独立启用的自动识别 SHALL 按其像素变化规则执行。

#### Scenario: Capture failure
- **WHEN** 目标窗口被最小化、销毁或 WGC 返回失败
- **THEN** 系统隐藏依赖该目标的覆盖层并显示失败原因与重试入口，不终止应用进程，不回退到屏幕截图

#### Scenario: Recognize a covered target
- **WHEN** 用户请求识别有效、可见、已恢复且可由 WGC 捕获的目标，但另一个窗口部分或完全遮挡它
- **THEN** 系统识别目标自身画面，不要求用户移除遮挡；合并遮挡比例超过 30% 时仅保存结果并隐藏浮层，30% 及以下仍可显示

#### Scenario: Tracking hook failure
- **WHEN** 跟踪 hook 注册或处理失败
- **THEN** 系统隐藏可能过期的覆盖层，记录英文诊断信息，并提供重试或重新选择入口

#### Scenario: Render a visible background target
- **WHEN** 目标窗口可见、已恢复且客户区合并遮挡比例不超过 30%，但目标没有获得前台焦点
- **THEN** 系统仍可显示当前有效覆盖层会话，不因 `IsForeground` 为 false 而拒绝渲染

#### Scenario: Reject an unavailable target
- **WHEN** 用户请求识别但目标不可见、已最小化、已销毁或没有有效客户区
- **THEN** 系统不调用 OCR，显示要求恢复目标或重新选择窗口的可操作提示，提示不把移除遮挡列为识别前提

#### Scenario: Capture is unsupported or times out
- **WHEN** WGC 支持检查失败或首个有效帧超时
- **THEN** 系统给出对应本地化提示，保留英文诊断原因并允许重试，不要求用户安装 OCR 模型来修复捕获故障

### Requirement: Resolve overlapping group hover deterministically
系统 SHALL 允许不同 phrase group 的几何重叠。当光标命中多个 group 时，系统 SHALL 优先选择引用 token 总数更少的 group；相同长度时 SHALL 选择 provider 返回顺序更靠前的 group。选中的 group SHALL 使用其应用 group ID 更新一个详情面板。

#### Scenario: Prefer the more specific group
- **WHEN** 光标同时命中一个短 group 和一个包含它的长 group
- **THEN** 系统显示短 group 的详情，并高亮短 group 的全部 parts

#### Scenario: Tie by provider order
- **WHEN** 多个命中 group 引用 token 数相同
- **THEN** 系统选择 provider 响应中更早出现的 group

### Requirement: 覆盖层内振假名的生命周期
系统 SHALL 在覆盖层渲染会话时，与词下划线一起绘制振假名文本；刷新与隐藏时 SHALL 与下划线一起清除已绘制元素，不留残留。临时隐藏 SHALL 保留保存的振假名会话数据，恢复时使用最新有效结果。振假名使用与下划线一致的屏幕坐标与 DPI 映射。

#### Scenario: 与下划线一起渲染振假名
- **WHEN** 覆盖层渲染一个包含汉字词的会话
- **THEN** 每个汉字词的振假名与它的下划线在同一渲染批次中出现

#### Scenario: 刷新与隐藏时清除振假名
- **WHEN** 覆盖层以新词刷新或隐藏
- **THEN** 所有已绘制的振假名随下划线一起清除，不留残留；临时隐藏不清除保存的会话结果

### Requirement: Keep the latest recognition session while occluded
系统 SHALL 允许遮挡期间完成的手动或自动识别更新当前目标保存的识别会话，并保留最新词线、振假名和短语结果。合并遮挡比例超过 30% 期间 overlay、popup 和区域选择 UI SHALL 保持隐藏且不接收 hover；比例回到 30% 及以下时 SHALL 使用最新会话及最新客户区几何恢复显示。显式结束会话或切换目标后，晚到的捕获/OCR/分析结果 SHALL NOT 恢复旧浮层。

#### Scenario: Finish recognition behind another window
- **WHEN** 目标合并遮挡比例仍超过 30% 时识别或短语分析完成
- **THEN** 系统保存当前目标的最新有效结果，不在遮挡窗口上方绘制任何标记或释义弹窗

#### Scenario: Restore the latest background result
- **WHEN** 目标合并遮挡比例回到 30% 及以下且隐藏期间已经完成新一轮有效识别
- **THEN** 系统显示最新会话而不是遮挡前旧会话，恢复动作本身不发起 OCR 或 LLM 请求

#### Scenario: Discard a result after explicit stop
- **WHEN** 用户已结束会话或切换目标后旧操作才完成
- **THEN** 系统丢弃旧结果，不重新打开旧浮层、不重启旧目标的自动识别

#### Scenario: Keep hover stable during non-geometry notifications
- **WHEN** 浮层已显示且目标仍可渲染，窗口通知未改变目标客户区矩形和 DPI
- **THEN** 系统维护目标相对 Z 顺序并保留当前悬停高亮和释义弹窗，不因重复、前台或层级通知反复隐藏并重新打开弹窗
