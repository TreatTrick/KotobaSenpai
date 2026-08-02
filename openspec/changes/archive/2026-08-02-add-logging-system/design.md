## Context

应用是一个 WPF 桌面日语学习伴侣（.NET 10），采用六边形/DDD 分层：`Core`（纯领域，`net10.0`，零外部依赖）、`Platform.Windows`（适配器，依赖 Core，目标 `net10.0-windows10.0.19041.0`）、`App`（组合根 + WPF 视图/ViewModel，依赖 Core 与 Platform）。架构测试（NetArchTest）钉死依赖方向：Core 不得依赖 Platform/App；Platform 不得依赖 App；ViewModel 不得引用 `System.Windows` 或 Platform.Windows。`TreatWarningsAsErrors` 全开。

`add-i18n`（已实现待归档）出于正确理由把 UI 中的 `catch { Status = $"…{ex.Message}" }` 改为按稳定错误码翻译的本地化消息：Core/Platform 异常实现 `IUserFacingException` 暴露 `ErrorCode`，`MainWindowViewModel.SetError` -> `App.Localization.UserMessageResolver.Resolve` 据码经 `IStringLocalizer` 翻译，原始异常文本不再嵌入 UI。代价是：异常的诊断细节（堆栈、内部异常、触发位置）在 `Resolve` 这一步被丢弃，且 `App.xaml.cs` 未注册任何全局未处理异常兜底，未捕获的错误静默崩溃无记录。

当前报错点分布：Core/Platform 抛出后不就地 `catch`，异常沿 `WordOverlayApplicationService.RecognizeAndShowAsync`（无 catch）传播到 `MainWindowViewModel` 的两个 `catch` 块（`Refresh` -> `WindowEnumerationFailed`、`RecognizeAsync` -> `RecognitionFailed`），二者均汇集到 `SetError`。具体抛出点包括 `WindowsOcrWordRecognizer`（`OcrLanguagePackMissing`、WinRT OCR/捕获异常）、`CapturedFrame`（`FramePixelDataTooShort`、`ArgumentOutOfRangeException`）、`CoordinateMapper`（`ArgumentOutOfRangeException`）、`OverlayTargetMustBeSpecifiedRule` -> `BusinessRuleValidationException`。

约束：不引入新 NuGet 包（保持 Core 零外部依赖与 BCL-only，与 `add-i18n` D1 一致）；不破坏依赖方向；项目原则要求"日志字段保留原文"并对 API Key、原始台词做脱敏（当前代码库尚无 API Key/LLM 流，属后续阶段）。

## Goals / Non-Goals

**Goals:**

- 在所有报错汇集点记录完整异常链（异常类型、消息、堆栈、内部异常），并自动附带 `add-i18n` 的稳定 `ErrorCode`，弥补错误码翻译路径丢弃的诊断细节。
- 提供按本地日期滚动的英文文件日志器，日志文件位于用户级 `%LocalAppData%`。
- 自动保留最近 7 天日志，过期文件自动删除（启动、跨日滚动两处触发）。
- 全局未处理异常兜底，确保未在本地捕获的错误也写入日志；记日志后向用户显示通用本地化错误提示，再受控退出（不静默崩溃）。
- 默认日志级别为 `Error` 及以上，可通过用户配置文件调整最小级别。
- 日志端口位于 Core、文件实现位于 App，与 `IStringLocalizer` 端口-适配器风格一致，不破坏架构测试。

**Non-Goals:**

- 不在本变更引入 API Key/原始台词脱敏实现--当前代码库无密钥/LLM 流；日志器结构不阻碍后续加入脱敏，但本变更不实现。
- 不引入 Microsoft.Extensions.Logging / Serilog 等 NuGet（见 D1）。
- 不在每个 `throw` 点重复记日志--异常传播到 catch 边界统一记录，避免重复（见 D2）。
- 不实现远程日志、结构化日志 sink、日志查看器 UI；日志级别用最小 `settings.json` 缝隙配置，不引入完整设置 UI（见 D9）。
- 不实现错误恢复（提示后仍退出，不"吞掉异常继续运行"）；恢复策略留待后续变更。
- 不本地化日志内容（日志全英文，与项目"日志字段保留原文"一致）。
- 不给 Core/Platform 在本变更注入日志器--它们的异常传播到 catch 边界即被记录；端口预留于 Core 供未来按需使用。

## Decisions

### D1：自定义 `ILogger` 端口在 Core（零依赖），不引入 Microsoft.Extensions.Logging

**选择：** 在 `KotobaSenpai.Core.Logging` 定义 `ILogger` 与 `LogLevel`；文件日志器实现位于 `KotobaSenpai.App.Logging`。

**理由：** `Core.csproj` 当前零 `PackageReference`，是项目刻意维持的纯领域边界；`Microsoft.Extensions.Logging.Abstractions` 是 NuGet 包，加入 Core 违背 `add-i18n` D1 已确立的"Core 零外部依赖、BCL-only"原则。MEL 在当前包图中并非传递依赖（App 仅引用 `CommunityToolkit.Mvvm` + `Microsoft.Extensions.DependencyInjection`，Platform 仅引用 Windows SDK）。一个约 30 行的自定义端口与现有 `IStringLocalizer` 端口-适配器风格完全一致，文件日志用 BCL `System.IO` 即可实现单 sink 按日滚动，不需要 MEL 的 provider/scopes/filter 生态。

**备选：** `Microsoft.Extensions.Logging` + `Serilog.Extensions.Logging.File`--新增 NuGet 且需把抽象放进 Core 或拆分，生态能力对单文件 sink 过重，否决。仅把抽象放 App（不放 Core）--见 D2，Platform 未来若需就地记日志将无法依赖 App 端口（架构测试禁止 Platform->App），故端口须在 Core。

### D2：在 catch 边界与全局兜底处记录，不在每个 throw 点重复记录

**选择：** 异常在 `MainWindowViewModel.SetError`（两个 catch 块的唯一汇集点）以 Error 级统一记录；`App.xaml.cs` 的三个全局未处理异常处理兜底记录。Core/Platform 的 throw 点不就地记日志。

**理由：** 当前 Platform/Core 抛出后不 `catch`、不转换，异常完整（含内部异常与堆栈）传播到 ViewModel catch 边界，在此记录一次即可捕获完整异常链，避免 throw 点与 catch 点重复记录同一错误。`SetError` 是所有错误路径的汇集点，在此记日志能用最小改动保证"所有报错的地方都要输出日志"且 DRY。WinRT OCR/捕获等底层异常同样传播到 catch 边界被记录，无信息丢失。

**备选：** 在每个 throw 点也记日志--重复记录、需向 Platform/Core 注入日志器、放大改动面，否决。仅在 `IUserMessageResolver.Resolve` 内记日志--把日志职责混入消息解析器，违反单一职责，否决（日志记在 ViewModel 边界更贴近触发用例的上下文）。

### D3：日志文件按本地日期滚动，位于 `%LocalAppData%/KotobaSenpai/logs`

**选择：** 路径 `%LocalAppData%/KotobaSenpai/logs/kotobasenpai-yyyy-MM-dd.log`，按本地日期（`DateTime.Today`）一天一个文件；写入时若当前日期与打开文件日期不同则滚动（关闭旧句柄、打开新文件）。每行格式：`2026-08-02T14:30:15.123+08:00 [ERR] (ErrorCode=OcrLanguagePackMissing) <消息>`，异常随后以多行转储（类型 + 消息 + 完整堆栈）。

**理由：** 用户要求"日志按照日期生成文件"，本地日期对单用户桌面应用最直观；`%LocalAppData%` 与 `add-i18n` 的 `settings.json` 同根（`%LocalAppData%/KotobaSenpai/`），权限与清理一致，且不随账户漫游（MVP 可接受）。ISO-8601 带偏移时间戳便于排序与跨时区阅读；多行异常转储保留完整堆栈，正是错误码路径丢弃的部分。

**备选：** UTC 日期--对单用户桌面应用不直观，且与"按日期生成文件"的用户心智不符，否决。单文件 + 内部按日分段--文件无限增长、清理困难，否决。文件名带序号/小时--粒度过细，用户要求按"日期"，否决。

### D4：7 天保留，启动 + 跨日滚动两处触发清理

**选择：** `LogRetentionPolicy.Cleanup` 扫描日志目录，按文件名解析日期（回退 `LastWriteTime`），删除日期早于 7 天的文件。在两个时机调用：(1) 应用启动；(2) 日志器跨日滚动打开新文件时。不引入后台定时器。

**理由：** 用户要求"日志只存最近 7 天，过期自动删除"，且明确不需要定时清理、启动清理即可。启动清理处理应用关闭期间过期的文件；跨日滚动在运行期首次写入新日期文件时顺带清理，覆盖长会话跨午夜的常见情形，且是事件触发（非后台定时器），零额外资源。桌面应用经常重启，启动清理已覆盖绝大多数过期文件；跨日滚动作为运行期补充，二者足够。扫描廉价（列目录 + 解析文件名）。

**备选：** 启动 + 跨日滚动 + 后台定时器（每小时）--用户明确否决定时清理，且定时器引入后台资源与额外释放逻辑，过度，否决。仅启动清理（去掉跨日滚动）--长会话跨午夜后过期文件要等到下次启动才删，体验略差；跨日滚动仅一行事件触发，保留成本极低，故保留。基于 `LastAccessTime`--Windows 可能禁用访问时间更新，不可靠，改用文件名日期 + `LastWriteTime` 回退。

### D5：全局未处理异常兜底记日志后向用户提示再受控退出

**选择：** `App.xaml.cs OnStartup` 注册 `DispatcherUnhandledException`、`AppDomain.CurrentDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`，三者均以 Error 级把异常写入日志并立即刷新。对会终止应用的未处理异常（`DispatcherUnhandledException`，以及 `AppDomain.UnhandledException` 的 `IsTerminating == true`），在记日志后向用户显示一个通用本地化错误提示（经 `IStringLocalizer` 取 `ResourceKeys.UnexpectedError` 等键，`zh-CN`/`en` 双语），用户确认后受控退出：`DispatcherUnhandledException` 置 `Handled = true` 并 `Shutdown(1)`；`AppDomain.UnhandledException` 经 `Application.Current.Dispatcher.Invoke` 尽力显示提示后让进程终止。`TaskScheduler.UnobservedTaskException`（默认不崩溃）仅记日志，不弹窗、不 `SetObserved`。

**理由：** 用户要求"给一个提示再崩溃"：未处理异常不应静默崩溃，应先告知用户。受控退出（`Handled = true` + `Shutdown`）而非裸崩溃（`Handled = false`）：避免在自定义提示之后再叠加 Windows 错误报告对话框，且能在退出前确保日志刷新。提示文案本地化以与 `add-i18n` 一致；诊断细节（堆栈）只在日志，不展示给用户。后台线程终止异常经 Dispatcher 回 UI 线程显示提示（此时 UI 线程通常空闲，因 UI 线程异常已先经 `DispatcherUnhandledException` 处理）；`UnobservedTaskException` 不弹窗避免对非致命后台任务骚扰。

**备选：** 裸崩溃（`Handled = false`）--提示后再弹 WER 对话框、日志可能未刷新，UX 与可靠性均差，否决。`Handled = true` 后继续运行--状态可能已损坏，掩盖故障，否决。所有处理器都弹窗（含 `UnobservedTaskException`）--对非致命未观察任务骚扰，否决。

### D6：英文日志；脱敏延后到 API Key/LLM 阶段

**选择：** 日志全部英文。代码库现有异常消息与规则 `Message` 本即为英文（如 `"Japanese OCR language pack not found."`、`"Overlay session target must be specified."`），`ErrorCodes` 为 locale 无关标识符，`ex.ToString()`/`ex.StackTrace` 天然英文。本变更不实现脱敏。

**理由：** 用户要求"日志全英语"，且与项目原则"日志字段保留原文"一致。当前代码库无 API Key、无 LLM、无原始台词流（属后续阶段），异常文本不含密钥，脱敏无对象；提前实现脱敏属于为不存在的数据路径过度设计。日志器经端口-实现分离，未来加入 `ILogRedactor` 装饰器不影响调用方。

**备选：** 现在就实现正则脱敏--无真实密钥格式可对齐，规则会失准，且增加无对应测试的复杂度，否决。中文日志--违背用户要求与项目原则，否决。

### D7：端口形态最小化；`ErrorCode` 由 App 实现自动提取

**选择：** Core 端口 `ILogger.Log(LogLevel, Exception?, string, params object[])` + `LogError`/`LogWarning`/`LogInformation` 便利重载；`LogLevel` 枚举（Trace/Debug/Information/Warning/Error/Critical）。端口不引用 `Core.Localization`（解耦）。`FileLogger`（App）在记录时检查 `exception is IUserFacingException`，自动在行首附加 `(ErrorCode=…)`。

**理由：** 端口最小化避免 Core.Logging 耦合 Core.Localization，保持各自单一职责；`ErrorCode` 提取是表现层关切（把领域标记接口翻译为日志字段），放 App 实现恰当，与 `UserMessageResolver` 把 `ErrorCode` 翻译为 UI 消息对称。便利重载让 ViewModel 调用简洁。

**备选：** 端口暴露 `ErrorCode` 参数--把 `Core.Localization` 概念引入 `Core.Logging`，耦合两个跨切面端口，否决。端口带 categories/scopes--单 sink 无需，过度设计，否决。

### D8：单例生命周期，`OnExit` 刷新释放

**选择：** `FileLogger` 注册为单例（持有当前文件 `StreamWriter`，必须共享）。`App.OnExit` 调用其 `Dispose` 刷新缓冲并关闭句柄。

**理由：** 文件句柄与写入锁必须单实例；多实例会争用同一文件。`OnExit` 刷新保证崩溃前已写入的日志不丢（`StreamWriter` 默认缓冲）。

### D9：默认日志级别 Error 及以上，经 `settings.json` 可配置

**选择：** `FileLogger` 持有最小日志级别，默认 `Error`。最小级别从 `%LocalAppData%/KotobaSenpai/settings.json` 的可选字段 `MinimumLogLevel`（字符串，如 `"Error"`/`"Warning"`/`"Information"`）读取，缺省或解析失败回退 `Error`。低于最小级别的日志条目（如 `LogWarning`/`LogInformation`）在 `FileLogger` 内被过滤，不写入文件。

**理由：** 用户要求默认 Error+ 且可配置。复用 `add-i18n` 已建立的 `settings.json` 最小持久化缝隙（同根 `%LocalAppData%/KotobaSenpai/`，BCL `System.Text.Json`，无新 NuGet），与"待设置模块落地后迁移"的既有约定一致。默认 `Error` 确保生产噪声最低；需要更详细诊断时改 `MinimumLogLevel` 即可。级别过滤属实现关切，放 `FileLogger`（App），Core 端口不感知最小级别。

**备选：** `appsettings.json` + `Microsoft.Extensions.Configuration`--新增 NuGet 且 WinExe 需 copy-to-output，违背现有零额外日志依赖风格，否决。环境变量 `KOTOBASENPAI_LOG_LEVEL`--可用但不如文件配置可发现、不与既有 settings 缝隙一致，作为备选保留。级别硬编码不可配--不满足用户要求，否决。

## Risks / Trade-offs

- **[并发写入]** OCR 在线程池异步执行，多线程可能同时写日志 -> `FileLogger` 内部用 `lock` 串行化所有写入；单 sink 无需无锁结构。
- **[磁盘/权限异常]** `%LocalAppData%` 不可写或磁盘满时日志器自身抛异常会拖垮主流程 -> 日志器内部 swallow 自身 IO 异常（或降级到 `Debug.Fail`/无操作），绝不让记日志抛出影响业务；加测试覆盖"日志目录不可写时不抛"。
- **[跨日滚动竞态]** 午夜前后多线程同时触发滚动 -> 滚动逻辑在 `lock` 内同步完成，确保同一日期仅打开一个句柄。
- **[清理误删]** 文件名解析日期失败可能误判 -> 仅删除文件名匹配 `kotobasenpai-yyyy-MM-dd.log` 且解析出日期早于 7 天的文件；不匹配的文件不删；加测试覆盖"非日志文件不删"与"恰好 7 天的保留、8 天的删除"边界。
- **[全局兜底与 DI 顺序]** `DispatcherUnhandledException` 等需在容器构建后、窗口显示前注册，handler 内访问 `ILogger`/`IStringLocalizer` 须容忍容器未就绪 -> handler 从 `App` 实例字段取已构建服务，若为 null 则跳过提示、仅尽力记日志（启动极早期崩溃无法提示，可接受）。
- **[非 UI 线程提示死锁]** `AppDomain.UnhandledException` 可能在非 UI 线程触发，直接弹窗有死锁风险 -> 经 `Application.Current.Dispatcher.Invoke` 回 UI 线程显示提示（UI 线程异常已先经 `DispatcherUnhandledException`，此时 UI 线程通常空闲）；若 Dispatcher 不可用则仅记日志不提示。
- **[日志不含密钥的假设]** 本变更假设当前异常文本无密钥；若后续阶段在异常消息中带入 API Key/台词，需先落地脱敏 -> 在 `add-llm` 等后续变更中显式加 `ILogRedactor`，并在其 spec 中标注依赖本日志端口。
- **[本地时区]** 按本地日期滚动在不同时区机器上日志归属日期不同 -> 对单用户桌面应用可接受；时间戳带偏移可还原绝对时刻。

## Migration Plan

- 纯代码变更，无数据/持久化迁移（日志目录此前不存在，首次运行自动创建）。实现后运行 `dotnet build`（`TreatWarningsAsErrors`）与全部测试（含架构测试）。
- 回滚：直接 revert 本变更；已生成的日志文件残留于 `%LocalAppData%/KotobaSenpai/logs/`，可手动删除，无 schema 残留。
- 现有测试不依赖日志；新增 `FileLoggerTests`、`LogRetentionPolicyTests` 与架构测试断言，随实现一并加入。

## Open Questions

- （两项开放问题已决议：日志级别默认 `Error` 且经 `settings.json` 可配，见 D9；全局兜底记日志后显示本地化提示再受控退出，见 D5。暂无遗留。）
