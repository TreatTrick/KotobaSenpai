## 1. 只读 ViewModel 与兼容性

- [x] 1.1 在 `PhrasePopupViewModel.cs` 中引入轻量内部只读 ViewModel，直接供 XAML 绑定，覆盖短语组、单词释义和无释义输入；保留字段顺序、可选字段规则、平假名与音调格式、成员顺序以及现有本地化资源键和回退。属性和成员条目构建后保持只读，不依赖 WPF 控件或 HWND，不增加 `PhrasePopupContent` 包装层、逐字段通知、可观察集合、命令或 MVVM 包。
- [x] 1.2 添加无需创建 WPF 窗口的 ViewModel 测试，覆盖三种模式、空可选字段和成员列表、已知与未知音调、本地化与回退标题，以及完整和稀疏输入产生独立只读实例且不改变先前实例。测试名称、消息和非语言样例数据保持英文。

## 2. 声明式弹窗视图

- [x] 2.1 新增 `PhrasePopup.xaml`，将现有窗口改为保持兼容的 `PhrasePopup.xaml.cs`；保留构造函数和公开显示、隐藏方法，初始化已编译 XAML，并将所有必要视图资源保留在 Platform 内部。
- [x] 2.2 在 XAML 中定义共享画刷、标题与区块样式、固定的可选区块布局、回退提示、现有滚动配置，以及成员词 `ItemsControl` 和 `DataTemplate`，保留当前外观和各模式内容顺序。
- [x] 2.3 让每个显示方法创建新的完整 `PhrasePopupViewModel`，通过一次 `DataContext` 赋值整体替换绑定源；XAML 直接绑定 ViewModel 属性，并通过可见性规则处理内容缺失；删除不再使用的 `Heading`、`Block`、`RenderWords` 以及命令式内容控件树构造路径。

## 3. 布局与原生行为

- [x] 3.1 在绑定和模板生效后，针对当前 XAML 根节点测量内容；保留 200/360 DIP 宽度策略、360 DIP 高度上限、现有尺寸预留量，以及锚点下方或上方的物理像素定位和现有显示器/DPI 重试逻辑。
- [x] 3.2 在视图侧保留非全局置顶、不可激活、鼠标穿透配置、原生函数导入与诊断、辅助 HWND 登记和注销，以及隐藏后复用行为；可使用少量 code-behind 或具体窗口行为类，ViewModel 不直接操作窗口与 HWND。保持纯定位计算可独立测试，确认无需改变 `WpfOverlayRenderer`、悬停时序和 Core 契约的行为。
- [x] 3.3 添加小范围 Windows STA 检查：不依赖 App 资源加载编译后的 XAML，确认 `DataContext` 为 `PhrasePopupViewModel` 并检查绑定字段和成员条目的生成；连续替换为短语组、单词和回退 ViewModel 时不残留旧区块，“短、长、短”内容的测量遵守现有尺寸限制。使用现有测试项目，仅在必要时启用 WPF 测试编译，不新增 UI 测试包。

## 4. 验证与交付依据

- [x] 4.1 运行 `dotnet test tests/KotobaSenpai.Platform.Windows.Tests/KotobaSenpai.Platform.Windows.Tests.csproj`，包含 ViewModel 测试、视图绑定检查、现有 `PhrasePopupPlacementTests` 和 `OverlayHoverTests`，修复本次变更引入的回归。
- [x] 4.2 运行 `dotnet test tests/KotobaSenpai.App.Tests/KotobaSenpai.App.Tests.csproj` 和 `dotnet build KotobaSenpai.slnx`，验证 XAML 编译和现有项目依赖方向。
- [x] 4.3 在 Windows 上进行冒烟验证，覆盖三种模式、完整内容切换至稀疏内容、长内容换行和高度限制、现有本地化标题、焦点保持、鼠标穿透、隐藏后复用、显示器边缘定位，以及可用的 100%/150%/200% DPI 配置。本次实际验证环境为 125%；100%/150%/200% 与跨 DPI 显示器移动未实测，限制已记录在 `validation.md`。
- [x] 4.4 在本变更的 `validation.md` 中记录命令、结果和无法覆盖的原生窗口或 DPI 场景；确认差异仅涉及弹窗展示及必要验证，然后运行 `openspec validate migrate-phrase-popup-to-xaml --strict`。
