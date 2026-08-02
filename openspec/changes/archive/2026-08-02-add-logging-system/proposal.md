## Why

应用目前没有任何日志系统。`add-i18n` 出于正确的理由把 UI 中的 `catch { Status = $"…{ex.Message}" }` 改为按稳定错误码翻译的本地化消息：用户不再看到原始异常文本。但代价是异常的诊断细节——堆栈、内部异常、触发位置与上下文——在 `MainWindowViewModel.SetError` → `IUserMessageResolver.Resolve` 这条路径上被彻底丢弃，开发和排障时无法回溯失败原因。同时 `App.xaml.cs` 未注册任何全局未处理异常兜底（`DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`），未在本地捕获的错误会静默崩溃且无任何记录。项目原则已要求"日志字段保留原文"（`VN-Learning-Project.md` 全局约束），本变更在不破坏 Core 领域纯度与依赖方向的前提下，引入一个跨切面的英文日志能力，把所有报错点统一写入按日期滚动、自动保留 7 天的日志文件。

## What Changes

- 新增 `ILogger` 日志端口与 `LogLevel` 枚举（Core，零外部依赖），定义 Error/Warn/Info 等级别与异常重载，作为与 `IStringLocalizer` 一致的跨切面端口。
- 新增按本地日期滚动的英文文件日志器实现 `FileLogger`（App），日志写入 `%LocalAppData%/KotobaSenpai/logs/kotobasenpai-yyyy-MM-dd.log`；跨日写入时自动滚动新文件。
- 新增 7 天保留与过期自动删除策略 `LogRetentionPolicy`（App）：在应用启动与跨日滚动时扫描日志目录，删除日期早于 7 天的日志文件（无后台定时器）。
- 在所有报错点接入日志：`MainWindowViewModel` 经构造注入 `ILogger`，在 `SetError`（所有错误路径的汇集点）记录 Error 级日志，含错误码（若异常实现 `IUserFacingException` 则自动提取 `ErrorCode`）、异常类型、消息与完整堆栈——保证"所有报错的地方都要输出日志"且不重复记录。
- 新增全局未处理异常兜底：`App.xaml.cs` 注册 `DispatcherUnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`，三者均以 Error 级写入日志并刷新；终止性未处理异常在记日志后向用户显示通用本地化错误提示（经 `IStringLocalizer`），用户确认后受控退出（`DispatcherUnhandledException` 置 `Handled = true` + `Shutdown`），不静默崩溃；`TaskScheduler.UnobservedTaskException` 仅记日志。
- 默认日志级别为 `Error` 及以上，可通过 `%LocalAppData%/KotobaSenpai/settings.json` 的 `MinimumLogLevel` 字段调整；缺省或非法值回退 `Error`，低于最小级别的条目被过滤。
- 在 DI 容器注册 `ILogger`（App 组合根，单例；文件日志器持有文件句柄，必须共享），`OnExit` 时刷新并释放。
- 新增架构测试：断言 `ILogger`/`LogLevel` 端口位于 Core、`FileLogger` 实现位于 App、Core/Platform 不依赖文件实现；新增 `FileLogger`（滚动、并发安全、错误码提取）与 `LogRetentionPolicy`（7 天清理）单元测试。

## Capabilities

### New Capabilities

- `logging`: 跨切面错误日志能力——定义日志端口与级别、按本地日期滚动的英文文件日志器、7 天保留与自动清理、全局未处理异常兜底，以及在所有错误汇集点记录完整异常链（含 `add-i18n` 的稳定错误码）。

### Modified Capabilities

无。`openspec/specs/` 当前为空（`phase1-window-word-overlay` 与 `add-i18n` 尚未归档）；本变更只新增日志能力，不改变 `window-word-overlay` 或 `localization` 的需求行为——仅在既有错误处理路径旁路记录日志，用户可见行为不变。

## Impact

- **新增文件**：`Core/Logging/ILogger.cs`、`Core/Logging/LogLevel.cs`（端口与枚举）；`App/Logging/FileLogger.cs`（按日期滚动实现）、`App/Logging/LogRetentionPolicy.cs`（7 天清理）、`App/Logging/LogConfiguration.cs`（最小级别配置读取）、`App/Logging/LogFileNaming.cs`（文件名与日期解析，按需）。新增测试 `KotobaSenpai.App.Tests/FileLoggerTests.cs`、`LogRetentionPolicyTests.cs`；扩展 `KotobaSenpai.Architecture.Tests/DependencyDirectionTests.cs`。
- **修改文件**：`App/ViewModels/MainWindowViewModel.cs`（注入 `ILogger`，在 `SetError` 记录日志）；`App/App.xaml.cs`（注册 `ILogger` 与 `LogConfiguration`、注册三个全局未处理异常兜底并显示本地化提示后受控退出、启动执行保留清理、`OnExit` 刷新释放）；`App/Resources/Strings.resx` 与 `Strings.zh-CN.resx`（新增崩溃提示本地化键）。
- **依赖**：不引入新 NuGet 包（保持 Core 零外部依赖与 BCL-only）；文件日志与清理用 BCL `System.IO` / `System.Threading` 实现。
- **架构**：不破坏依赖方向——`ILogger`/`LogLevel` 端口位于 Core，文件实现在 App；ViewModel 仅依赖 Core 端口；Platform 经异常传播到 catch 边界被记录，无需在本变更注入日志器。
