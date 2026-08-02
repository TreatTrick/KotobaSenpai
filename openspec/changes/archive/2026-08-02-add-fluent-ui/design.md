## Context

应用是一个 WPF 桌面日语学习伴侣（.NET 10），采用六边形/DDD 分层：`Core`（纯领域，`net10.0`，零外部依赖）、`Platform.Windows`（适配器，依赖 Core）、`App`（组合根 + WPF 视图/ViewModel，依赖 Core 与 Platform）。架构测试（NetArchTest）钉死依赖方向：Core 不得依赖 Platform/App；Platform 不得依赖 App；ViewModel 不得引用 `System.Windows` 或 Platform.Windows。`TreatWarningsAsErrors` 全开。

当前 UI 极简：`MainWindow.xaml` 用硬编码颜色画边框与正文，`App.xaml` 仅有一个占位 Button 样式。既有跨切面能力已落地：`localization`（`{loc:Loc}` 标记扩展 + `LanguageService` 经 `MainWindow` 的 DependencyProperty 暴露给视图，不入 ViewModel）、`window-word-overlay`（Platform.Windows 透明置顶覆盖层）、`logging`（settings.json 的 `MinimumLogLevel` 缝隙）。`%LocalAppData%/KotobaSenpai/settings.json` 已是既定最小持久化缝隙（BCL `System.Text.Json`）。

本变更原拟用 `MaterialDesignThemes`，经用户决策改为 `WPF-UI` 的 Win11 Fluent 风格：本项目为 Windows 单平台应用，Fluent 是 OS 母语设计语言，且原生支持 Mica 云母背景、系统强调色与**跟随系统深浅主题**（`SystemThemeWatcher`），比 Material 更贴合运行环境与"Win11 设置"观感。

约束：Core 必须保持零外部依赖；ViewModel 不得引用 `System.Windows`；不破坏既有依赖方向与架构测试；不改变 `window-word-overlay`、`localization`、`logging` 的需求行为。

## Goals / Non-Goals

**Goals:**

- 为 WPF 主壳建立 Win11 Fluent 视觉主题：`FluentWindow` + Mica 云母标题栏 + WPF-UI 控件主题，替换硬编码颜色。
- 采用 Win11 设置卡片布局（`ui:Card` 分组目标窗口/外观/操作）。
- 支持主题模式运行时切换（`Auto` 跟随 OS / `Light` / `Dark`），偏好持久化到 settings.json 并启动恢复；`Auto` 经 WPF-UI `SystemThemeWatcher` 跟随系统深浅。
- WPF-UI 依赖仅落在 App 视图层；Core 零外部依赖、ViewModel 不引用 `System.Windows` 不变，并以架构测试钉死。
- 本地化（`{loc:Loc}`）在 Fluent 控件上行为不变，含运行时切换文化即时刷新。

**Non-Goals:**

- 不改造悬浮覆盖层（Platform.Windows 的 `WpfOverlayRenderer`）--透明置顶、点击穿透的工具窗口，且 Platform 不可引用 App 的 WPF-UI；其 `DeepSkyBlue` 下划线暂不动。
- 不读取/跟随系统强调色（accent）--仅跟随 OS 深/浅；WPF-UI 默认按系统强调色着色部分控件，但本变更不自定义强调色逻辑。
- 不引入 WPF-UI `NavigationView`/抽屉/多页导航--当前只有一个主壳，后续设置面板再按需引入。
- 不改 Core/Platform 任何代码与端口；不改变窗口选择/OCR/覆盖层/日志的行为。
- 不再使用 `MaterialDesignThemes`（本变更移除）。

## Decisions

### D1：引入 `WPF-UI`（仅 App），移除 `MaterialDesignThemes`

**选择：** 在 `KotobaSenpai.App` 引用 `WPF-UI`；移除 `MaterialDesignThemes`/`MaterialDesignColors`；Core/Platform 不引用。

**理由：** 本项目 Windows 单平台，Fluent 是 OS 母语设计语言，WPF-UI 是 WPF 生态事实标准的 Fluent 库（`FluentWindow`、Mica/Acrylic 背景、`Card`/`ToggleSwitch`/`TitleBar`、`ApplicationThemeManager`/`SystemThemeWatcher`）。App 已有外部 NuGet（`CommunityToolkit.Mvvm`、`Microsoft.Extensions.DependencyInjection`），新增视图层 UI 库与之同层，不违背 Core 零外部依赖。用户明确要求 Win11 设置风格，WPF-UI 直接提供该观感。

**备选：** 继续用 `MaterialDesignThemes`--跨平台语言但在 Windows 桌面观感"外来"，且不原生跟随系统主题/Mica，已被用户否决。手写 Fluent XAML--工作量与保真度不可控，否决。迁移到 WinUI 3--需更换项目模型与互操作，规模远超本变更，否决。

### D2：WPF-UI 依赖仅落 App；主题服务为视图层，不入 ViewModel

**选择：** `WPF-UI` 仅被 `KotobaSenpai.App` 引用。主题应用由 App 视图层服务 `FluentThemeService`（引用 `ApplicationThemeManager`/`SystemThemeWatcher`）承担，经 `MainWindow` 暴露给视图（DependencyProperty），**不**注入 ViewModel。ViewModel 不持有主题状态、不引用 `System.Windows`/WPF-UI。

**理由：** 架构测试禁止 ViewModel -> `System.Windows`，而 `ApplicationThemeManager`/`SystemThemeWatcher` 是 WPF-UI（基于 `System.Windows`）的 API。既有 `LanguageService` 已为同样原因经 `MainWindow` DependencyProperty 暴露给视图而非注入 ViewModel；主题沿用该模式。

**备选：** 主题状态放 ViewModel--违反架构测试，否决。WPF-UI 放 Core--破坏 Core 零外部依赖，否决。

### D3：主题模式持久化复用 settings.json（沿用既有基础设施）

**选择：** `%LocalAppData%/KotobaSenpai/settings.json` 的可选 `Theme` 字段（`"Auto"`/`"Light"`/`"Dark"`），由既有 `IThemePreferenceStore`/`LocalAppDataThemePreferenceStore` 读写，经 `LocalAppDataSettingsFile` 可变 `JsonObject` 读-改-写保留 `Language`/`MinimumLogLevel`。缺省或非法值回退 `Auto`。

**理由：** 该持久化基础设施库无关，原为 Material 方案所建，Fluent 方案完整复用，无需改动。复用 `add-i18n`/`add-logging-system` 已建立的 settings.json 缝隙，无新 NuGet。

### D4：三种主题模式（Auto/Light/Dark），默认 Auto 跟随 OS

**选择：** 主题模式为 `Auto`/`Light`/`Dark` 三态，默认 `Auto`。`Auto` 跟随 Windows 系统深浅；`Light`/`Dark` 为显式覆盖，选择后停止跟随直至重新选 `Auto`。外观卡片内放置主题 ComboBox。

**理由：** 用户要求"跟随系统 OS 自动变深色浅色"。`Auto` 默认使应用开箱随系统；保留 `Light`/`Dark` 覆盖以满足偏好。三态让用户能自由固定到任一主题。

### D5：`FluentWindow` + Mica + 自定义标题栏（本变更做自定义 chrome）

**选择：** `MainWindow` 继承 `ui:FluentWindow`，`ExtendsContentIntoTitleBar=True` + `WindowBackdropType=Mica` + `ui:TitleBar` 自定义标题栏（标题 + 系统最小/最大/关闭按钮）。

**理由：** 与 Material 方案保留 OS chrome 不同，Fluent 的标志性观感正是 Mica 云母背景与融合标题栏；WPF-UI 的 `FluentWindow` 已封装 Mica/圆角/标题栏融合，边界情况由库处理。Win11 上 Mica 透出桌面壁纸，Win10 自动回退为纯色，均可用。

**备选：** 保留标准 OS 标题栏--丢失 Mica 融合观感，违背 Fluent 风格选择，否决。

### D6：OS 主题跟随用 WPF-UI `SystemThemeWatcher` + `ApplicationThemeManager`，移除自定义注册表/SystemEvents

**选择：** `FluentThemeService`：`Auto` 模式调用 `ApplicationThemeManager.ApplySystemTheme()` + `SystemThemeWatcher.Watch(window, Mica, true)`（应用当前系统主题并订阅系统变化）；`Light`/`Dark` 调用 `SystemThemeWatcher.UnWatch(window)` + `ApplicationThemeManager.Apply(ApplicationTheme.Light/Dark, Mica, true)`（停止跟随并固定）。不再使用 `Microsoft.Win32.Registry`/`SystemEvents`。`FluentThemeService` 不实现 `IDisposable`：`SystemThemeWatcher` 随窗口关闭/进程退出由 WPF-UI 自动清理；在 `OnExit` 显式 `UnWatch` 会因窗口句柄已销毁抛 `InvalidOperationException`，故不在退出时做清理。

**理由：** WPF-UI 内置 `SystemThemeWatcher`/`GetSystemTheme`/`ApplySystemTheme` 专门处理系统主题检测与跟随，比自定义注册表读取 + `SystemEvents.UserPreferenceChanged` 更可靠、更简洁，且随库更新适配新 Windows 版本。移除 `Microsoft.Win32` 注册表/SystemEvents 依赖，降低复杂度与跨线程处理。Mica 背景随主题正确切换由 WPF-UI 统一管理。

**备选：** 保留自定义注册表 + SystemEvents--与 WPF-UI 内置能力重复且更脆弱，否决。

### D7：架构测试钉死 WPF-UI 仅在 App

**选择：** 扩展 `KotobaSenpai.Architecture.Tests`：断言 `Wpf.Ui` 程序集仅被 `KotobaSenpai.App` 引用（Core/Platform 不引用）；重申 ViewModel 不引用 `System.Windows` 且不依赖 `KotobaSenpai.App.Themes`（既有断言应仍通过）。

**理由：** 与 `add-i18n`/`add-logging-system` 一致，把依赖方向约束以架构测试钉死。原 Material 方案的 `MaterialDesign` 断言改为 `Wpf.Ui` 断言。

### D8：主题模式 ComboBox 经 `{loc:Loc}` 本地化，选择变化在代码后置处理

**选择：** 外观卡片内主题选择为 `ComboBox`，其 `ComboBoxItem` 的 `Content` 经 `{loc:Loc Key=ThemeMode_Auto/Light/Dark}` 本地化（文化切换时由 `LocalizationHost` 自动刷新），`Tag` 携带模式字符串。`SelectionChanged` 在 `MainWindow` 代码后置解析 `Tag` 调用 `FluentThemeService.SetMode`；启动时按持久化模式选中对应项。不进入 ViewModel。

**理由：** ComboBox 是 Win11 设置中最自然的模式选择器（如"选择你的模式"）。`ComboBoxItem` 直接用 `{loc:Loc}` 内容实现免费本地化与运行时刷新，无需枚举->本地化包装类或值转换器。`Tag`+代码后置保持 ViewModel 纯净（与 `LanguageService` 同理，主题为视图层关切）。

## Risks / Trade-offs

- **[新外部依赖]** App 新增 `WPF-UI` 传递依赖 -> 锁定版本、按需 review；App 本就有外部 NuGet，可接受。Core 零依赖不变（D2/D7 钉死）。
- **[Mica 平台差异]** Mica 仅 Win11 支持，Win10 回退纯色 -> WPF-UI 自动处理回退，可接受。
- **[WPF-UI API 演进]** `FluentWindow`/`ApplicationThemeManager`/`SystemThemeWatcher` 的方法签名在不同大版本略有差异 -> 锁定 4.3.0，实现时按 4.3.0 实际签名调用（`Watch(Window,WindowBackdropType,Boolean)`、`Apply(ApplicationTheme,WindowBackdropType,Boolean)`）。
- **[覆盖层未主题化]** 悬浮覆盖层仍用 `DeepSkyBlue` 下划线 -> 有意为之（Platform 不可引用 App WPF-UI），列为 Non-Goal。
- **[本地化标记 × Fluent 样式]** `{loc:Loc}` 须在 Fluent 控件上继续生效 -> 样式与绑定正交，验证运行时切换文化能即时刷新（spec 场景覆盖）。
- **[架构测试回归]** 向 App 加 WPF-UI 不得触发现有测试 -> 新增正向断言"仅 App 引用 `Wpf.Ui`"（D7），并确认既有 ViewModel->System.Windows 断言仍通过。
- **[主题 ComboBox 与 OS 跟随交互]** `Auto` 模式下系统切换深浅时模式仍为 `Auto`（仅实际主题变），ComboBox 选中项不变 -> 符合预期（选中项反映模式而非实际主题）。

## Migration Plan

- 纯代码/UI 变更，无数据迁移。`settings.json` 的 `Theme` 字段（`Auto`/`Light`/`Dark`）沿用：既有值（如 `Auto`）继续生效；缺省回退 `Auto`，无破坏。
- 实现后运行 `dotnet build`（`TreatWarningsAsErrors`）与全部测试（含架构测试），并做启动冒烟（确认 `FluentWindow`+Mica 加载、主题应用、settings 持久化保留 `Language`）。
- 回滚：直接 revert 本变更；`settings.json` 的 `Theme` 字段被旧代码忽略（无害），无 schema 残留。

## Open Questions

- 暂无遗留。（Mica 在 Win10 回退、`Wpf.Ui` 4.3.0 API 签名均已在实现前确认。）
