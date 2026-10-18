## ADDED Requirements

### Requirement: 声明式弹窗展示

弹窗 SHALL 在编译后的 WPF XAML 中声明窗口外观、内容布局、共享样式、可选区块可见性和成员词条模板。C# 显示方法 SHALL 提供展示数据并执行窗口行为，不再构造标题、详情区块或成员词控件树。现有默认外观 SHALL 保留。

#### Scenario: 维护弹窗外观

- **WHEN** 贡献者调整标题字体、区块间距或成员词颜色
- **THEN** 对应展示由弹窗的 XAML 资源或模板定义，无需修改 C# 控件工厂

#### Scenario: 独立于 App 资源加载

- **WHEN** 未提供 App 专用资源字典，直接构造 Platform 弹窗
- **THEN** 编译后的 XAML 能够加载，所有必要展示资源均可用，且无需 Platform 引用 App

### Requirement: 只读 ViewModel 直接绑定

弹窗 SHALL 使用轻量、只读的 `PhrasePopupViewModel` 作为 XAML 的 `DataContext`。ViewModel SHALL 负责三种展示模式的字段、格式化文案、可选区块状态和有序成员词列表；其属性及成员条目 SHALL 在构建完成后保持只读。弹窗 SHALL NOT 额外引入 `PhrasePopupContent` 及仅转发展示属性的包装层。当前按次整体替换模式 SHALL NOT 要求逐字段通知、可观察集合、命令或新增 MVVM 包。

ViewModel SHALL 独立于 WPF 控件和原生窗口，不持有或操作 `Window`、`FrameworkElement`、`UIElement`、`Brush`、HWND 或窗口几何。实际布局测量、DPI 与原生窗口操作 SHALL 由视图侧负责。

#### Scenario: 独立构建并验证展示逻辑

- **WHEN** 为短语组、单词释义或无释义输入构建 `PhrasePopupViewModel`
- **THEN** 无需创建 WPF 窗口即可检查该实例的模式、字段、格式化文案、可选区块状态和成员顺序

#### Scenario: 绑定只读展示实例

- **WHEN** 显示方法构建本次 `PhrasePopupViewModel` 并赋给窗口的 `DataContext`
- **THEN** XAML 直接绑定该实例的只读属性，无需额外内容包装层或逐字段变更通知

### Requirement: 保留现有三种内容模式

弹窗 SHALL 保留现有公开构造函数、显示方法，以及短语组、单词释义和无释义单词的字段顺序、可选字段规则与文本格式。读音 SHALL 继续使用现有平假名转换，音调 SHALL 使用提供的有序 notation 或现有音调未知回退。

#### Scenario: 显示短语组详情

- **WHEN** `ShowResult` 接收到标题、释义、语法和有序成员词释义
- **THEN** 弹窗显示标题、非空释义和语法区块，以及按输入顺序排列的成员词区块
- **AND** 每个成员词使用现有格式显示词头、可选读音、音调、可选词性和可选释义

#### Scenario: 隐藏缺失的短语组内容

- **WHEN** 短语组的释义或语法为空，且没有成员词
- **THEN** 对应区块及其标题均不占据布局空间

#### Scenario: 显示有释义的单词

- **WHEN** `ShowWordMeaning` 接收到单词释义
- **THEN** 弹窗先显示词头与读音标题，再依次显示可选词性、音调、可选释义和可选语法

#### Scenario: 显示无释义的单词

- **WHEN** `ShowWordWithoutMeaning` 接收到单词
- **THEN** 弹窗显示词头与读音标题、已知或未知音调以及现有无释义提示，不显示释义、语法或成员词区块

### Requirement: 复用时完整替换内容

每次显示调用 SHALL 根据本次输入创建新的、完整的 `PhrasePopupViewModel`，并在测量和定位弹窗前，通过一次 `DataContext` 赋值整体替换绑定源。新输入中缺失的字段和成员条目 SHALL NOT 残留上一次调用的可见内容。隐藏后的弹窗 SHALL 仍可通过现有显示方法复用。

#### Scenario: 在内容模式间切换

- **WHEN** 同一个弹窗先显示内容完整的短语组，再显示单词释义，最后显示无释义回退
- **THEN** 每次显示绑定新的 ViewModel 实例，仅包含当前模式的内容，不残留旧区块标题、成员条目或字段值，先前实例的只读内容保持不变

#### Scenario: 隐藏后再次显示

- **WHEN** 调用 `HidePopup` 后，后续显示方法提供了新内容
- **THEN** 现有弹窗实例能够显示新内容，无需新增公共生命周期接口或修改调用方

### Requirement: 保留 Platform 层接口处的文案解析

ViewModel 构建逻辑 SHALL 继续通过注入的 `IStringLocalizer` 解析现有本地化标题、音调未知文本和无释义文本；未提供本地化器时，保留现有回退。XAML SHALL 将这些字符串作为 ViewModel 属性绑定。本次迁移 SHALL NOT 修改文案、资源归属或现有直接写入的区块标题。

#### Scenario: 为一次显示调用解析文案

- **WHEN** 显示调用提供了本地化器
- **THEN** 本地化字段使用现有资源键和本次调用返回的值，读音与音调数据保持现有格式

#### Scenario: 未提供本地化器时显示

- **WHEN** 显示调用未提供本地化器
- **THEN** 弹窗显示现有英文回退标题和提示，XAML 加载不会失败

### Requirement: 根据当前绑定内容确定尺寸和位置

弹窗 SHALL 在计算期望窗口尺寸前测量新绑定内容，包括已生成的成员词条目。弹窗 SHALL 保留现有宽度策略，即最小 200 DIP、最大 360 DIP，高度上限 360 DIP，以及换行、滚动容器配置和现有尺寸预留量。最终物理像素位置 SHALL 受所选显示器工作区约束，包括负坐标显示器和靠近底部边缘时改放锚点上方的回退。

#### Scenario: 短内容切换为长内容后再切回

- **WHEN** 同一个弹窗依次显示短内容、包含多个成员词的长内容，再次显示短内容
- **THEN** 每次尺寸计算均使用当前内容，不使用过期绑定或模板，并遵守现有尺寸限制

#### Scenario: 在显示器边缘或 DPI 变化后定位

- **WHEN** 锚点靠近所选显示器底部或右侧边缘，或弹窗移动到不同 DPI 的显示器
- **THEN** 现有物理像素定位与有限次 DPI 重试使弹窗保持在该显示器工作区内，必要时放到锚点上方

### Requirement: 保留原生窗口交互和登记

弹窗 SHALL 保持非全局置顶、不可激活和鼠标穿透的工具窗口行为，保留当前 WPF 与原生标志。其 HWND SHALL 在窗口源初始化后向 `OverlayWindowRegistry` 登记，并在关闭时注销。迁移 SHALL 保留原生定位诊断，且 SHALL NOT 修改覆盖层悬停检测或时序。

#### Scenario: 在目标窗口上方显示

- **WHEN** 现有覆盖层渲染器显示弹窗
- **THEN** 弹窗不抢占焦点、不出现在任务栏中，鼠标点击继续传递给下方窗口

#### Scenario: 登记并关闭弹窗

- **WHEN** 弹窗的原生窗口源完成初始化，随后窗口被关闭
- **THEN** 其 HWND 通过现有辅助窗口注册表登记，并在关闭后注销
