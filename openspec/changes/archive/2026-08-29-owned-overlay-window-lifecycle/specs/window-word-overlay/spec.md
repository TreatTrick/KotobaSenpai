## ADDED Requirements

### Requirement: Use an owned target-relative overlay window

系统 SHALL 将覆盖层创建为透明、点击穿透、不可激活的 Win32 owned top-level WPF 窗口，其 owner SHALL 为当前 Aokana 目标 HWND，且 SHALL 使用 `Topmost = false`。覆盖层 SHALL 跟随 owner 的生命周期和目标相邻的 Z 顺序，不得浮在遮挡目标的其他窗口之上。目标失去前台焦点但仍可见且未被遮挡时，覆盖层 SHALL 继续允许显示。

#### Scenario: Attach overlay to selected target

- **WHEN** 用户为有效的 Aokana HWND 显示一个识别会话
- **THEN** 系统将 overlay HWND 的 owner 设置为该目标 HWND，关闭全局置顶，并在目标客户区上方以非激活、点击穿透方式显示词线和假名

#### Scenario: Aokana loses foreground focus

- **WHEN** Aokana 仍可见且未被其他窗口遮挡，但 KotobaSenpai 或其他不遮挡 Aokana 的窗口获得焦点
- **THEN** 覆盖层不因目标失去前台状态而清空或隐藏当前会话

#### Scenario: Another window occludes Aokana

- **WHEN** 另一个可见窗口与 Aokana 客户区存在任意正面积交集且位于 Aokana 上方
- **THEN** 系统将目标视为遮挡，覆盖层隐藏或保持在遮挡窗口下方，不得在遮挡窗口上绘制目标标记

## MODIFIED Requirements

### Requirement: Word underline overlay

系统 SHALL 在目标窗口客户区上方显示透明、点击穿透的覆盖层,并为每个本地分词词的**每个 rect** 在其对应行的词框下方绘制横线(跨行词因此有多条下划线,各贴各自行底);对每个已验证 phrase group,系统 SHALL 为其每个 part 的每个行内几何绘制可识别的组合词块标记。覆盖层 SHALL 使用目标 HWND owner 和非置顶 Z 顺序，目标被其他窗口遮挡时不得显示在遮挡窗口上方。覆盖层刷新时 SHALL 整体替换当前词和 group 列表,隐藏时 SHALL 清除所有标记并恢复点击穿透。悬停一个词的**任一 rect** 时,系统 SHALL 高亮该词的**全部 rect**(整词一起变色);悬停 group 的任一 part 时,系统 SHALL 高亮该 group 的全部 parts。启用短语分析时，覆盖层 MAY 先显示本地振假名而暂不绘制下划线；分析批次完成后的刷新 SHALL 仅为成功句段中的词绘制下划线。关闭短语分析时 SHALL 保持原有本地下划线行为。目标窗口移动、缩放或 DPI 变化时，覆盖层 SHALL 按目标跟踪快照重映射已有归一化几何，不得自动重新 OCR；目标被隐藏、最小化或遮挡时 SHALL 隐藏覆盖层并停止 hover 交互但保留当前会话，目标恢复可见且未遮挡时 SHALL 恢复当前会话；目标失效、销毁或切换时 SHALL 清理当前会话。

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

- **WHEN** 短语分析已启用但尚未完成
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
- **THEN** 覆盖层使用相对于原客户区保存的归一化几何重新定位和缩放所有横线、短语标记与假名，不重新 OCR、不重新请求 LLM 且不产生累计缩放误差

#### Scenario: Respect target occlusion

- **WHEN** 另一个窗口覆盖目标窗口的任意正面积区域
- **THEN** 覆盖层隐藏或按目标窗口的 Z 顺序处于遮挡窗口下方，不在遮挡窗口上显示目标的横线或假名，并保留当前会话

#### Scenario: Hide temporarily without losing the session

- **WHEN** 目标窗口被隐藏、最小化或被其他窗口遮挡，但目标 HWND 仍然有效
- **THEN** 覆盖层立即隐藏、停止 hover、关闭 popup，但保留当前词线、假名、短语标记和归一化会话数据供恢复使用

#### Scenario: Restore after target becomes visible

- **WHEN** 目标窗口恢复可见且不再被遮挡
- **THEN** 覆盖层按最新客户区快照恢复显示当前会话的几何，不自动执行 OCR 或 LLM 分析

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

系统 SHALL 将目标窗口不可见、最小化、销毁、被其他窗口遮挡、捕获失败、OCR 不可用、覆盖层创建失败和实时跟踪失败转换为用户可理解的错误状态，并允许用户重新选择窗口或重试当前窗口。目标不可见、最小化或被其他窗口遮挡时，显式识别 SHALL 被拒绝并提示用户先显示并恢复目标窗口且移除遮挡；覆盖层显示不要求目标处于前台，但要求目标处于可见、已恢复且未被遮挡状态。系统 SHALL 不自动重新 OCR。

#### Scenario: Capture failure

- **WHEN** 目标窗口被最小化、销毁或 Graphics Capture 返回失败
- **THEN** 系统隐藏覆盖层并显示失败原因与重试入口，不终止应用进程

#### Scenario: Recognize a covered target

- **WHEN** 用户请求识别但目标窗口不可见、已最小化或被其他窗口遮挡
- **THEN** 系统不调用 OCR 屏幕捕获，显示可操作提示要求用户先显示目标窗口并移除遮挡

#### Scenario: Tracking hook failure

- **WHEN** 跟踪 hook 注册或处理失败
- **THEN** 系统隐藏可能过期的覆盖层，记录英文诊断信息，并提供重试或重新选择入口

#### Scenario: Render a visible background target

- **WHEN** 目标窗口可见、已恢复且未被遮挡，但目标没有获得前台焦点
- **THEN** 系统仍可显示当前有效覆盖层会话，不因 `IsForeground` 为 false 而拒绝渲染
