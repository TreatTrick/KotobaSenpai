## Why

应用主界面当前是裸 WPF 默认样式：`MainWindow.xaml` 用硬编码颜色（`#DDD`/`#555`/`#333`）画边框与正文，`App.xaml` 仅有一个设置 `Margin`/`Padding` 的 Button 样式，没有色彩体系、没有字号层级、没有控件主题，控件观感与交互反馈缺失。本项目是 **Windows 单平台** WPF 桌面应用（MVP1 只支持 Windows），因此采用与 OS 母语一致的 Windows 11 Fluent 风格（WPF-UI）而非跨平台 Material：Fluent 原生支持 Mica 云母背景、系统强调色、并**原生跟随系统深浅主题**（`SystemThemeWatcher`），与 Windows 11 设置面板观感一致，比 Material 更贴合本应用的运行环境。

## What Changes

- 在 App 工程引入 `WPF-UI` NuGet 包（仅视图层），并移除此前引入的 `MaterialDesignThemes`/`MaterialDesignColors`；Core 保持零外部依赖、Platform 不受影响。
- 在 `App.xaml` 合并 WPF-UI 资源字典（`ThemesDictionary` + `ControlsDictionary`），提供 Fluent 控件隐式样式与基准主题。
- 重构 `MainWindow.xaml` 为 `ui:FluentWindow`：`ExtendsContentIntoTitleBar` + `WindowBackdropType=Mica` + `ui:TitleBar` 自定义标题栏；内容区采用 Win11 设置卡片布局（`ui:Card` 分组"目标窗口/外观/操作"），控件使用 WPF-UI 样式；以主题画刷替换硬编码颜色。
- 主题模式切换改为 `Auto`/`Light`/`Dark` 三态（默认 `Auto`）：外观卡片内放置主题 ComboBox，偏好持久化到 `%LocalAppData%/KotobaSenpai/settings.json`（复用既有 settings 缝隙）。`Auto` 经 WPF-UI `SystemThemeWatcher.Watch` 原生跟随 Windows 系统深浅；`Light`/`Dark` 经 `ApplicationThemeManager.Apply` 显式覆盖并 `UnWatch` 停止跟随。
- 新增 `FluentThemeService`（App 视图层，替代原 `MaterialThemeService`）：经 `ApplicationThemeManager`/`SystemThemeWatcher` 应用主题，经 `IThemePreferenceStore` 持久化；不注入 ViewModel。
- 新增架构测试：断言 `Wpf.Ui` 仅被 App 引用（Core/Platform 不引用），ViewModel 仍不引用 `System.Windows` 与主题服务。
- 验证既有行为不受影响：`{loc:Loc}` 本地化绑定在重构后的控件上仍生效；窗口选择、OCR、悬浮覆盖层、日志行为不变。

## Capabilities

### New Capabilities

- `fluent-ui`: Windows 11 Fluent 视觉主题能力--为 WPF 主壳建立 Mica 云母标题栏、Fluent 控件主题、Win11 设置卡片布局，以及持久化到 settings 的主题模式切换（`Auto` 跟随 OS / `Light` / `Dark`，经 WPF-UI `SystemThemeWatcher`/`ApplicationThemeManager`）。

### Modified Capabilities

无。`window-word-overlay` 的悬浮覆盖层是透明置顶、点击穿透的工具窗口（位于 Platform.Windows），不属于标准 chrome UI，本变更不触及；`localization` 的需求行为不变（标签仍按 `{loc:Loc}` 本地化，仅观感重构）。

## Impact

- **新增文件**：`App/Themes/FluentThemeService.cs`（主题模式 -> `ApplicationThemeManager`/`SystemThemeWatcher` 应用 + 偏好持久化）。`AppThemeMode`/`IThemePreferenceStore`/`LocalAppDataThemePreferenceStore`/`LocalAppDataSettingsFile` 沿用（库无关）。
- **删除文件**：`App/Themes/MaterialThemeService.cs`（被 `FluentThemeService` 取代）。
- **修改文件**：`App/KotobaSenpai.App.csproj`（移除 `MaterialDesignThemes`，新增 `WPF-UI`）；`App/App.xaml`（WPF-UI 资源字典）；`App/MainWindow.xaml`（FluentWindow + 卡片重构）；`App/MainWindow.xaml.cs`（继承 `FluentWindow`、主题 ComboBox 处理）；`App/App.xaml.cs`（注册 `FluentThemeService`、启动 `Initialize(window)`）；`App/Resources/ResourceKeys.cs` 与 `Strings.resx`/`Strings.zh-CN.resx`（新增 `Label_Theme`/`Label_Appearance`，移除未用的 `Tooltip_ThemeMode`）；`KotobaSenpai.Architecture.Tests/DependencyDirectionTests.cs`（断言改为 `Wpf.Ui` 仅 App）。
- **依赖**：新增 NuGet `WPF-UI`（仅 App），移除 `MaterialDesignThemes`/`MaterialDesignColors`。Core 零外部依赖原则不受影响。
- **架构**：不破坏依赖方向--WPF-UI 仅在 App；ViewModel 仍不引用 `System.Windows`；Platform 与悬浮覆盖层不变。
