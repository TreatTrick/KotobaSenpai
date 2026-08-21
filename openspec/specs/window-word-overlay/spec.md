# window-word-overlay Specification

## Purpose
TBD - created by archiving change phase1-window-word-overlay. Update Purpose after archive.
## Requirements
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
系统 SHALL 在目标窗口上方显示透明置顶覆盖层,并为每个本地分词词的**每个 rect** 在其对应行的词框下方绘制横线(跨行词因此有多条下划线,各贴各自行底);对每个已验证 phrase group,系统 SHALL 为其每个 part 的每个行内几何绘制可识别的组合词块标记。覆盖层刷新时 SHALL 整体替换当前词和 group 列表,隐藏时 SHALL 清除所有标记并恢复点击穿透。悬停一个词的**任一 rect** 时,系统 SHALL 高亮该词的**全部 rect**(整词一起变色);悬停 group 的任一 part 时,系统 SHALL 高亮该 group 的全部 parts。启用短语分析时，覆盖层 MAY 先显示本地振假名而暂不绘制下划线；分析批次完成后的刷新 SHALL 仅为成功句段中的词绘制下划线。关闭短语分析时 SHALL 保持原有本地下划线行为。

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
系统 SHALL 将窗口销毁、捕获失败、OCR 不可用和覆盖层创建失败转换为用户可理解的错误状态，并允许用户重新选择窗口或重试当前窗口。

#### Scenario: Capture failure
- **WHEN** 目标窗口被最小化、销毁或 Graphics Capture 返回失败
- **THEN** 系统隐藏覆盖层并显示失败原因与重试入口，不终止应用进程

### Requirement: Resolve overlapping group hover deterministically
系统 SHALL 允许不同 phrase group 的几何重叠。当光标命中多个 group 时，系统 SHALL 优先选择引用 token 总数更少的 group；相同长度时 SHALL 选择 provider 返回顺序更靠前的 group。选中的 group SHALL 使用其应用 group ID 更新一个详情面板。

#### Scenario: Prefer the more specific group
- **WHEN** 光标同时命中一个短 group 和一个包含它的长 group
- **THEN** 系统显示短 group 的详情，并高亮短 group 的全部 parts

#### Scenario: Tie by provider order
- **WHEN** 多个命中 group 引用 token 数相同
- **THEN** 系统选择 provider 响应中更早出现的 group

### Requirement: 覆盖层内振假名的生命周期
系统 SHALL 在覆盖层渲染会话时，与词下划线一起绘制振假名文本；刷新与隐藏时 SHALL 与下划线一起清除，不留残留。振假名使用与下划线一致的屏幕坐标与 DPI 映射。

#### Scenario: 与下划线一起渲染振假名
- **WHEN** 覆盖层渲染一个包含汉字词的会话
- **THEN** 每个汉字词的振假名与它的下划线在同一渲染批次中出现

#### Scenario: 刷新与隐藏时清除振假名
- **WHEN** 覆盖层以新词刷新或隐藏
- **THEN** 所有已绘制的振假名随下划线一起清除，不留残留

