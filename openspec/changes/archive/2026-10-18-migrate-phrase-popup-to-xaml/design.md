## Context

`PhrasePopup` 是 `KotobaSenpai.Platform.Windows` 中公开的 WPF `Window`。`WpfOverlayRenderer` 通过 `ShowResult`、`ShowWordMeaning`、`ShowWordWithoutMeaning` 和 `HidePopup` 复用同一个实例。目前，每个显示方法都会清空 `StackPanel`，再通过 `Heading`、`Block` 和 `RenderWords` 创建控件。

这个类还负责测量内容、将原生 HWND 定位到物理像素坐标的锚点附近、把窗口限制在所选显示器的工作区内、设置不可激活和鼠标穿透样式，并向 `OverlayWindowRegistry` 登记窗口。这些行为与界面的固定结构相互独立，迁移后必须保留。Platform 项目已设置 `UseWPF=true` 并引用 Core；App 引用 Platform，并持有本地化资源。

## Goals / Non-Goals

**目标：**

- 让弹窗布局、共享字体样式、颜色、换行和成员词外观可以直接在 XAML 中调整。
- 使用轻量、只读的 `PhrasePopupViewModel` 直接承载绑定数据，集中管理展示模式、字段、格式化文案和成员词列表，并与窗口行为分离。
- 保留三种显示模式、可选字段规则、内容顺序、格式以及本地化查询和回退行为。
- 保留现有公开构造函数和方法、原生定位、窗口生命周期以及隐藏后复用行为。
- 验证绑定内容替换后，界面和测量尺寸都已更新，再执行窗口定位。

**非目标：**

- 迁移 `OverlayWindow`、重写下划线或振假名绘制，或修改悬停检测和时序。
- 重新设计弹窗、添加交互控件、引入主题设置，或修改文案及翻译。
- 新增通用 MVVM 框架、UI 抽象层、新包，或让 Platform 引用 App。
- 修改 OCR/LLM 处理，或承诺渲染性能提升。

## Decisions

### 1. 使用单个 XAML 窗口，并在窗口内定义资源和模板

新增 `Overlay/PhrasePopup.xaml`，将现有类移至 `PhrasePopup.xaml.cs`，改为保持兼容的分部类。在注册生命周期处理逻辑前调用 `InitializeComponent`。在 XAML 中声明窗口现有外观和 WPF 属性、滚动容器、标题、可选详情区块、回退提示以及成员词 `ItemsControl`。可复用画刷、标题和区块样式、成员词 `DataTemplate` 均放在窗口自身资源中，确保无需 App 资源也能加载。

公共区块使用共享的固定布局，成员词使用一个条目模板。保留各模式的内容顺序：短语组为“标题、释义、语法、成员词”；单词为“标题、词性、音调、释义、语法”；回退为“标题、音调、无释义提示”。

其他方案：继续保留 C# 控件工厂会让展示定义分散；只把根容器移到 XAML，主要维护成本仍然存在；拆成三个窗口或三套大型模式模板，会重复定义公共外观。单个声明式视图足以覆盖当前差异。

### 2. 使用只读 ViewModel，每次显示时整体替换

在 `PhrasePopupViewModel.cs` 中引入轻量内部 ViewModel，不依赖 WPF。由它负责将三种显示模式的输入映射为只读展示属性，包括标题、已格式化的文案、可选区块状态和有序成员词列表。ViewModel 及其成员条目在构建完成后保持只读，不包含 `UIElement`、`Brush`、原生句柄或屏幕几何数据。复用 `Kana.ToHiragana`、现有 `PitchAccentText` 和音调未知回退，不重新实现语言分析。

每个现有显示方法根据本次输入创建完整的 `PhrasePopupViewModel`，通过一次 `DataContext` 赋值整体替换窗口的绑定源。未提供的字段和成员列表明确设为缺省或空集合，确保从内容完整的短语组切换到单词或回退模式时，不会残留旧区块。XAML 使用 `Mode=OneTime` 绑定 ViewModel 的只读属性，`DataContext` 替换时仍会重新求值，并通过普通触发器表达可见性；映射代码不创建控件。模板内部对视图属性 `Tag` 的绑定保留常规更新，以便区块标题随新的展示文案刷新。

ViewModel 同时保留“按次构建完整快照”的更新方式，直接作为窗口的 `DataContext`。不再引入独立的 `PhrasePopupContent`，也不在展示数据外增加仅转发属性的 ViewModel 包装层。当前没有对象内部的持续字段变更或编辑交互，因此无需实现逐字段 `INotifyPropertyChanged`、使用可观察集合、添加命令或引入 MVVM 包及依赖注入注册。

选择这个方案，是为了明确展示逻辑的归属，并方便独立测试三种模式的字段、格式化和完整替换。与直接绑定同等职责的内容快照相比，核心机制接近，没有天然的性能优势。其他方案包括：使用单独的内容 DTO，会让展示职责的命名不够明确；长期复用可变 ViewModel，会引入当前不需要的通知与字段清理规则；直接更新命名控件，则会把清理规则分散到多个显示方法中。

### 3. 保持依赖方向和文案解析方式

构建 ViewModel 时，继续通过注入的 Core `IStringLocalizer` 解析现有资源键；未提供本地化器时，使用现有英文回退。现有直接写入的区块标题继续保留，本次迁移不修改本地化策略或文案。XAML 将所有标题和提示作为数据绑定，不新增硬编码的可本地化文本。

不引用 `KotobaSenpai.App.Localization.LocExtension`，不将 `.resx` 文件移入 Platform，也不新增反向项目引用。这样可以保持 Platform 窗口独立构造，并避免项目循环依赖。

### 4. 区分展示状态与窗口行为，并测量当前内容

`PhrasePopupViewModel` 负责展示状态和内容；WPF 布局测量、原生 HWND 操作和窗口生命周期由视图侧负责。对于当前规模，优先使用少量 code-behind；如果这些代码影响阅读，也可委托给具体的窗口行为类，无需为此引入通用窗口服务框架。

保留 `Position`、`CalculatePlacement`、`SetClickThrough`、原生函数导入、日志、辅助窗口登记与注销，以及公开的显示和隐藏方法签名。继续保持不可激活、工具窗口、鼠标穿透和非全局置顶行为，以及锚点下方优先、空间不足时放到上方的定位策略。隐藏后仍可复用窗口，关闭时注销 HWND。ViewModel 不直接持有或操作 `Window`、`FrameworkElement` 和 HWND。

`CalculatePlacement` 这类只接收矩形和尺寸、返回定位结果的纯计算，可以保持为独立函数并直接测试，无需依赖 code-behind 实例，也无需放入 ViewModel。真正应用定位结果、调用 `SetWindowPos` 的操作仍由视图侧执行。

调整 `ShowAndPosition`：只有在新绑定及成员词模板已经生成可测量的内容后，才测量 XAML 内容根节点。在读取期望尺寸前，通过现有 UI 调度器处理必要的延迟绑定和布局工作，避免测量旧的子控件树。保留现有宽度策略，即最小 200 DIP、最大 360 DIP，高度上限 360 DIP，以及现有尺寸预留量和基于物理像素的显示器定位逻辑，包括 DPI 变化后的有限次重试。不要把现有 `Pad` 尺寸预留量悄悄改成新的视觉内边距。

其他方案：以 WPF `Left`/`Top` 替换原生定位流程、仅使用 `SizeToContent`，或改为 WPF `Popup`，都会改变坐标、尺寸或 HWND 生命周期语义，本次迁移没有必要采用。

### 5. 验证展示语义及实际 WPF 视图

通过无需创建 WPF 窗口的 ViewModel 测试，覆盖各模式的字段显示规则、成员顺序、平假名和音调格式、本地化回退，以及为完整或稀疏输入构建独立只读实例。保留 `PhrasePopupPlacementTests` 和 `OverlayHoverTests`。增加小范围 Windows STA 检查，验证已编译 XAML 的加载、以 ViewModel 作为 `DataContext` 的绑定、成员词模板的生成，以及连续替换 ViewModel 时的内容清理和长短内容尺寸更新。避免引入截图基线，也不要让断言依赖控件树偶然的嵌套层级。

运行 Platform 和 App 测试，并构建整个解决方案。在 Windows 上进行冒烟验证，覆盖三种模式、鼠标穿透与不可激活、隐藏后复用、屏幕边缘定位，以及环境支持的 100%/150%/200% DPI。明确记录无法执行的手工验证，不把单元测试当作原生窗口行为的验证依据。

## Risks / Trade-offs

- [测量前绑定的成员条目尚未生成] → 在绑定和布局生效后测量当前 XAML 根节点，并通过 STA 检查验证长短内容替换。
- [上一种模式的数据残留到下一次显示] → 每次构建完整只读 ViewModel 并整体替换 `DataContext`，测试“完整短语组、单词、回退”的连续切换。
- [XAML 意外依赖 App 资源或额外的构造激活机制] → 将资源放在窗口内部，保留可调用的构造函数，并从 Platform 验证窗口加载。
- [新布局间距改变裁剪或定位结果] → 保留默认外观和尺寸策略，检查换行、高度限制、显示器边缘和混合 DPI 移动。
- [迁移扩展为文案或视觉重新设计] → 以现有查询方式、字符串和外观为基准，此类调整另行确定范围。

## Migration Plan

先确认当前内容展示行为，引入只读 `PhrasePopupViewModel` 及其独立测试，再以编译后的 XAML 和兼容后台代码替换命令式视图；各显示方法创建新的 ViewModel 并整体替换 `DataContext`，最后运行自动化和 Windows 验证。所有显示路径改为绑定后，删除不再使用的 `Heading`、`Block` 和 `RenderWords` 工厂方法。无需迁移设置或持久化数据，回滚时还原代码即可。

## Open Questions

无阻塞问题。本提案仅覆盖 `PhrasePopup` 展示层；实现时需要记录实际可用的 DPI 和显示器冒烟验证条件。
