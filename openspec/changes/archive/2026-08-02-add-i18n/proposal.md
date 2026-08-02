## Why

应用当前所有用户可见文案——XAML 标签、ViewModel 状态文本、以及通过 `catch { Status = $"…{ex.Message}" }` 漏到 UI 的异常消息——均为硬编码简体中文，无法切换英语。项目路线图已将英语母语日语学习者列为后续用户群，且异常消息直接拼入状态栏。现在引入国际化可在不破坏 Core 领域纯度的前提下原生支持简体中文与英语，并让领域/平台异常通过稳定错误码由表现层翻译，为后续多语言扩展奠定基础。

## What Changes

- 新增 `IStringLocalizer` 端口（Core）与 `ResourceManager` 实现（App）：支持运行时切换 `CurrentUICulture`，并通过 `CultureChanged` 事件通知订阅者即时刷新。
- 新增 `Strings.resx`（英文，中性回退）与 `Strings.zh-CN.resx`（简体中文）资源文件，集中存放 App 层用户可见文案与插值占位符。
- 新增自定义 `LocExtension` XAML 标记扩展，使静态标签在运行时切换语言后即时更新，无需重启。
- 新增稳定错误码 `ErrorCodes` 与异常 `ErrorCode` 属性；Core/Platform 异常不再携带本地化文本，改由 App 表现层按码翻译为本地化消息（**BREAKING**：`WindowsPlatformException` 构造函数与 `IBusinessRule` 接口签名变更）。
- ViewModel 注入 `IStringLocalizer`，替换硬编码状态文案，并订阅 `CultureChanged` 重算已显示的本地化属性。
- 新增语言选择 UI 与最小持久化（`%LocalAppData%/KotobaSenpai/settings.json`），启动时恢复用户语言偏好。
- 更新现有测试中硬编码中文断言，改用 localizer fake 或错误码断言；新增架构测试确保 localizer 实现位于 App、端口位于 Core。

## Capabilities

### New Capabilities

- `localization`: 管理应用界面语言资源、运行时文化切换，以及将领域/平台异常错误码映射为本地化用户消息。

### Modified Capabilities

无。`openspec/specs/` 当前为空（phase1 尚未归档），无既有能力的需求层级变更；本变更只新增跨切面能力，不改变 `window-word-overlay` 的需求行为。

## Impact

- **新增文件**：`Core/Localization/IStringLocalizer.cs`、`Core/Localization/ErrorCodes.cs`；`App/Resources/Strings.resx`、`Strings.zh-CN.resx`；`App/Localization/ResourceManagerStringLocalizer.cs`、`LocExtension.cs`、`LanguageService.cs`、`IUserMessageResolver.cs`（异常码→消息映射）。
- **修改文件**：`WindowsPlatformException.cs`（加 `ErrorCode`）；`IBusinessRule.cs`、`BusinessRuleValidationException.cs`、`OverlayTargetMustBeSpecifiedRule.cs`（加 `ErrorCode`）；`CapturedFrame.cs`、`WindowsOcrWordRecognizer.cs`（抛带码异常）；`MainWindowViewModel.cs`（注入 localizer、替换文案、订阅切换）；`MainWindow.xaml`（标签改 `{Loc}`、加语言切换控件）；`App.xaml.cs`（注册 localizer、启动 culture）；相关测试文件。
- **依赖**：仅使用 BCL `System.Resources.ResourceManager` / `CultureInfo`，不引入新 NuGet 包。
- **架构**：不破坏现有依赖方向——Core 仍零外部依赖，ViewModel 仍不引用 `System.Windows`/Platform.Windows；`IStringLocalizer` 作为端口位于 Core，实现在 App。
