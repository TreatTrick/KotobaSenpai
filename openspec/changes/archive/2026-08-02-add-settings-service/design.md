## Context

应用是一个 WPF 桌面日语学习伴侣（.NET 10），采用六边形/DDD 分层：`Core`（纯领域，`net10.0`，零外部依赖）、`Platform.Windows`（适配器，依赖 Core）、`App`（组合根 + WPF 视图/ViewModel，依赖 Core 与 Platform）。架构测试（NetArchTest）钉死依赖方向：Core 不得依赖 Platform/App；Platform 不得依赖 App；ViewModel 不得引用 `System.Windows` 或 Platform.Windows。`TreatWarningsAsErrors` 全开。组合根 `App.xaml.cs` 用 `Microsoft.Extensions.DependencyInjection` 按端口注册。

用户设置文件 `%LocalAppData%/KotobaSenpai/settings.json` 是跨多个变更逐步建立的"最小持久化缝隙"：`add-i18n` 引入 `Language` 字段与静态助手 `LocalAppDataSettingsFile`（读-改-写保留未知字段），`add-logging-system` 引入 `MinimumLogLevel` 字段（`LogConfiguration` 自带文件读取，**未复用助手**），`add-fluent-ui` 引入 `Theme` 字段（`LocalAppDataThemePreferenceStore` 复用助手）。现状产出三处直接读写文件的生产代码：

- `LocalAppDataSettingsFile`（App.Localization，`internal static`）：`LoadOrEmpty()` / `Save(JsonObject)` 助手。
- `LocalAppDataLanguagePreferenceStore` / `LocalAppDataThemePreferenceStore`（App.Localization）：经助手读-改-写各自字段，含 Enum/空白校验。端口 `ILanguagePreferenceStore` / `IThemePreferenceStore` 亦在 App.Localization，注释写明"待设置模块落地后迁移"。
- `LogConfiguration`（App.Logging，`public static`）：自带 `DefaultPath` + `File.Exists` + `JsonDocument.Parse` + `catch IOException/JsonException`，**绕过助手重复实现文件读取**。

消费者侧是干净的：`LanguageService` 依赖 `ILanguagePreferenceStore`、`FluentThemeService` 依赖 `IThemePreferenceStore`，二者均不直接碰文件；`App.xaml.cs` 在组合根调 `LogConfiguration.LoadMinimumLevel()` 配 `FileLogger`。即问题集中在"文件 I/O 散落在多个生产类型、且至少一处重复实现"，而非消费者耦合。

约束：不引入新 NuGet（保持 Core 零外部依赖与 BCL-only）；不破坏依赖方向；不改变既有持久化行为（键、文件位置、回退规则不变）；不引入设置 UI（沿用 `add-logging-system` D9、`add-i18n` 既有非目标）。

## Goals / Non-Goals

**Goals:**

- 把 `settings.json` 的读写收拢到唯一一个可注入服务，使各功能不再各自读文件、写文件，消除 `LogConfiguration` 的重复文件读取逻辑。
- 端口位于 Core、文件实现位于 App，与 `IStringLocalizer` / `ILogger` 跨切面端口-适配器风格一致，可经架构测试钉死。
- 端口可经 in-memory fake 测试，使 stores / `LogConfiguration` 的恢复与解析逻辑脱离磁盘验证。
- 保留未知字段、容忍文件缺失与损坏的既有契约不变（`Language`/`Theme`/`MinimumLogLevel` 共存互不覆盖；缺失/损坏视为空对象不抛）。
- 既有 `settings.json` 原样继续生效，零数据迁移。

**Non-Goals:**

- 不引入设置 UI（设置值的编辑仍由各功能自身的 UI 承担，如语言 ComboBox、主题 ComboBox；本变更只统一底层读写）。
- 不引入 schema 版本字段或迁移框架（`add-i18n` 已留"未来格式演进再加版本字段"的约定；当前三字段不变）。
- 不引入新 NuGet（`Microsoft.Extensions.Configuration` / `Options` 等被否决，见 D7）。
- 不改变语言/主题/日志级别的持久化键、文件位置或回退规则（属实现重构，非行为变更）。
- 不实现设置变更通知/订阅（`ISettingsService` 不暴露变更事件；当前无消费者需要，预留后续）。
- 不支持运行期外部进程编辑 `settings.json`（服务为唯一归属，懒加载后不重读；见 Risks）。

## Decisions

### D1：集中 `ISettingsService` 端口位于 Core，文件实现位于 App

**选择：** 在 `KotobaSenpai.Core.Settings` 定义 `ISettingsService` 端口；在 `KotobaSenpai.App.Settings` 定义 `SettingsService` 文件实现。

**理由：** 设置是真正的跨切面关注点（日志、语言、主题三家共用，未来更多），与已确立的 `IStringLocalizer`（Core.Localization）、`ILogger`/`LogLevel`（Core.Logging）跨切面端口-适配器风格完全一致：端口放 Core 使其成为领域可依赖的纯抽象，文件 I/O 与 `%LocalAppData%` 路径属基础设施关切放 App 实现层，Core 零外部依赖原则不受影响（端口签名不引入 JSON/IO 类型）。端口入 Core 也使 NetArchTest 能以与 logging/localization 相同的形态钉死归属，并允许未来 Core 领域服务按需读取配置而无需跨越边界重构。注意：端口入 Core 并不意味着 Core 当前消费它--与 `ILogger` 一样，端口入 Core 是为边界纯净与未来可用，非为当下 Core 调用。

**备选：** 端口与实现均在 App（与 `IThemePreferenceStore`/`ILanguagePreferenceStore` 这些特性级持久化端口同处 App.Localization）--更省一个 Core 文件夹，但设置与"某项偏好"不同：它是跨多功能的通用基础设施抽象，放 App 会与 logging/localization 的跨切面端口先例不一致，且未来 Core 领域服务想读配置时将无端口可依（架构测试禁止 Core 依赖 App），故否决。

### D2：端口形态为字符串键 / 字符串值，最小且不引用领域类型

**选择：** `ISettingsService` 暴露 `string? GetValue(string key)` 与 `void SetValue(string key, string? value)` 两个方法。当前所有设置值在文件中本就是字符串（`Language` 为 culture 名、`Theme` 为枚举名、`MinimumLogLevel` 为级别名）；类型化解析（`Enum.TryParse<AppThemeMode>`、`LogLevel` 解析、空白校验）保留在各自的薄门面（stores / `LogConfiguration`）内。

**理由：** 端口保持领域类型无关（不引用 `AppThemeMode`、`LogLevel`、`CultureInfo`），单一职责为"按键存取原始字符串值"，与 `LocalAppDataSettingsFile` 既有能力对齐，迁移代价最小。类型化是各功能的关切（`AppThemeMode` 属 App.Themes、`LogLevel` 属 Core.Logging），下沉到通用端口会把跨切面端口耦合到各领域类型，违反端口最小化原则（与 `add-logging-system` D7"端口形态最小化、`ErrorCode` 由 App 实现提取"同理）。

**备选：** 通用 `T? GetValue<T>(string key)` / `void SetValue<T>(string key, T value)` 泛型端口--更"类型安全"，但实现需在端口层做 `System.Text.Json` 反序列化（把 JSON 关切引入服务边界），且当前设置全是字符串、无复杂对象，泛型属为不存在的需求过度设计，否决。按字段拆方法（`GetLanguage()`/`GetTheme()`/`GetMinimumLogLevel()`）--把领域概念焊进通用设置端口，违反单一职责且每加一项设置就要改端口，否决。

### D3：单例、懒加载到内存 + 写穿（write-through），内部 `lock` 串行化

**选择：** `SettingsService` 注册为单例。首次 `GetValue`/`SetValue` 时懒加载 `settings.json` 为内存 `JsonObject`（文件缺失/损坏视为空对象）；`GetValue` 从内存对象读；`SetValue` 更新内存对象并**立即写穿**到磁盘（含目录自动创建）。所有读/写在一个 `lock` 内串行化。

**理由：** 单例 + 懒加载 + 写穿正是"唯一归属"的自然形态：服务持有文件的内存视图，不再像今天那样每次 `Save` 都整文件重读（stores 现状是每次 `Load` + `Save` 各一次完整 I/O）。写穿保证 `SetValue` 后立即落盘、跨重启可见（与既有读-改-写语义一致）。`lock` 串行化覆盖 UI 线程写（语言/主题切换）与组合根读（启动取日志级别）的潜在交错，与 `FileLogger` 的并发安全处理同风格。懒加载而非启动即读，避免在极早期启动路径（容器构建前）触发磁盘 I/O。

**备选：** 每次 `GetValue`/`SetValue` 都整文件读-改-写（不缓存）--严格保持"外部编辑可见"的当前行为，但单例服务每次都重读文件有违"唯一归属"的收拢初衷，且无消费者实际依赖运行期外部编辑，否决（见 Risks 的对应权衡）。启动即全量加载并常驻--等价于懒加载但把 I/O 提前到启动关键路径，无收益，否决。

### D4：保留特性级 stores 为 `ISettingsService` 之上的薄类型化门面

**选择：** `LocalAppDataLanguagePreferenceStore` / `LocalAppDataThemePreferenceStore` 改为构造注入 `ISettingsService`，`Load`/`Save` 委托服务读写 `Language`/`Theme` 键并保留既有 Enum/空白校验。`ILanguagePreferenceStore` / `IThemePreferenceStore` 端口接口与消费者（`LanguageService` / `FluentThemeService`）**不变**。

**理由：** stores 提供的是类型化 + 校验的偏好视图（`AppThemeMode?` / `string?` 含 `Enum.TryParse` 与空白判断），与服务提供的原始字符串存取是不同职责：服务=文件 I/O 归属，store=领域类型解析。保留 stores 为薄门面使 `LanguageService`/`FluentThemeService` 的依赖与既有单元测试（用 in-memory fake store）**零改动**，把变更面收在文件 I/O 收拢这一件事上，符合"最小破坏"。

**备选：** 删除 stores，让 `LanguageService`/`FluentThemeService` 直接依赖 `ISettingsService` 并内联字符串键与解析--把原始键（`"Language"`/`"Theme"`）散布到消费者、丢失类型化封装、且需改动两个服务及其测试，改动面更大且降低封装，否决。让 stores 仍直接读文件（只统一 `LogConfiguration`）--未真正收拢，用户诉求未满足，否决。

### D5：`LogConfiguration` 降为接收 `ISettingsService` 的薄桥，删除自带文件逻辑

**选择：** `LogConfiguration.LoadMinimumLevel` 改签名为接收 `ISettingsService`（如 `LoadMinimumLevel(ISettingsService settings)`），经 `settings.GetValue("MinimumLogLevel")` 取字符串再 `Enum.TryParse` 为 `LogLevel`，缺省/非法回退 `Error`。删除其 `DefaultPath`、`File.Exists`、`JsonDocument.Parse`、`catch IOException/JsonException`。`App.xaml.cs` 中 `FileLogger` 的注册 lambda 改为 `LogConfiguration.LoadMinimumLevel(sp.GetRequiredService<ISettingsService>())`。

**理由：** 这是本变更要消除的核心重复：`LogConfiguration` 此前完整复制了 `LocalAppDataSettingsFile` 的文件读取 + 容错逻辑。改为依赖端口后，文件 I/O 与容错归属 `SettingsService` 单一出处，`LogConfiguration` 只剩"字符串 -> `LogLevel` 解析 + 回退"这一日志关切，职责清晰且可经 fake `ISettingsService` 单测（与 stores 同）。`FileLogger` 本身不直接依赖 `ISettingsService`，避免把设置关切混入日志器构造（日志器只接收已解析的 `LogLevel`，与现状一致）。

**备选：** 让 `FileLogger` 直接注入 `ISettingsService` 在构造时自取最小级别--把设置依赖混入日志器，且 `FileLogger` 构造签名变长，违反"日志器只关心级别"的既有设计，否决。保留 `LogConfiguration` 的 `filePath` 可注入重载用于测试--测试改用 fake `ISettingsService` 后该重载无存在必要，删除以减负。

### D6：删除静态助手 `LocalAppDataSettingsFile`

**选择：** 删除 `App/Localization/LocalAppDataSettingsFile.cs`，其 `LoadOrEmpty`/`Save` + 文件路径 + 目录创建 + 容错逻辑由 `SettingsService` 吸收（路径常量随之迁入 `SettingsService`）。

**理由：** 助手为 `internal static`，不可注入、不可 mock，是设置散落的根因之一；其职责被 `SettingsService` 完全覆盖后无存在必要。删除也强制所有调用方改走端口，避免新旧两条路径并存。助手原位于 `Localization` 命名空间亦属错置（设置非本地化关切），迁出后命名空间更内聚。

**备选：** 保留助手作为 `SettingsService` 的私有静态细节--多一层无意义转发，且 `internal static` 仍可能被未来代码绕过端口直接调用，否决。

### D7：不引入 `Microsoft.Extensions.Configuration` / `Options`，沿用 BCL `System.Text.Json`

**选择：** `SettingsService` 用 BCL `System.Text.Json.Nodes.JsonObject`（与既有助手一致）做读-改-写保留未知字段，不引入任何配置框架 NuGet。

**理由：** 与 `add-i18n` D1、`add-logging-system` D1/D9 已确立的"Core 零外部依赖、BCL-only、复用既有 settings 缝隙、无新 NuGet"原则一致。当前设置是单文件、扁平字符串键值、无嵌套/绑定/变更通知需求，`Microsoft.Extensions.Configuration` + `Options` 的 provider/binding 生态对单文件桌面应用过重，且需在 WinExe 下处理 `copy-to-output`，违背既有零额外依赖风格。

**备选：** `Microsoft.Extensions.Configuration` + `ConfigurationBuilder`（JSON provider）+ `IOptions<T>`--新增 NuGet、引入强类型绑定样板、WinExe 需 `copy-to-output`，且 `IOptions` 的变更通知在本应用无消费者，过度，否决。

### D8：DI 注册顺序与单例生命周期

**选择：** `App.xaml.cs ConfigureServices` 注册 `services.AddSingleton<ISettingsService, SettingsService>()`，置于 `FileLogger` 注册之前（逻辑顺序，MS.DI 解析顺序由依赖图决定，`FileLogger` lambda 内 `sp.GetRequiredService<ISettingsService>()` 在解析 `FileLogger` 时才触发 `SettingsService` 构造，无需物理前置，但注册集中放置便于阅读）。`SettingsService` 单例与 `FileLogger`/stores 单例共享同一实例。

**理由：** 设置服务持内存状态（懒加载的 `JsonObject`），必须单例；stores 与 `LogConfiguration` 经由 DI 取同一单例，保证"唯一内存视图"成立。MS.DI 的 lambda 工厂在解析时求值，`SettingsService` 会在首个 `FileLogger` 解析时被构造，时序自然正确。

**备选：** 在 `OnStartup` 显式 `GetRequiredService<ISettingsService>()` 预热--多一次手动调用，无实际收益（懒加载已足够），否决。

## Risks / Trade-offs

- **[运行期外部编辑不可见]** `SettingsService` 懒加载后持有内存视图，不再每次重读文件；若用户在应用运行时手动编辑 `settings.json`，应用不会感知，且下次 `SetValue` 会以内存视图写穿、覆盖外部改动 -> 应用是设置的唯一预期编辑者（各功能 UI 才是编辑入口），运行期手动编辑不在支持范围；在 spec/design 明示此假设。备选"每次重读"被 D3 否决。
- **[并发写]** UI 线程切换语言/主题与组合根读最小级别可能交错 -> 所有 `GetValue`/`SetValue` 在单例内部 `lock` 内串行化，与 `FileLogger` 并发模型同风格。
- **[DI 时序]** `FileLogger` 解析依赖 `ISettingsService`，若注册缺失会在启动抛 `InvalidOperationException` -> 注册集中放置并有架构/冒烟测试覆盖；`SettingsService` 构造不触发磁盘 I/O（懒加载），启动早期无副作用。
- **[向后兼容]** 改动文件 I/O 归属可能引入行为漂移（如容错分支不一致） -> `SettingsService` 复刻既有助手的容错契约（缺失/损坏视为空对象、目录自动创建、写回保留未知字段），并由 `SettingsServiceTests` 覆盖原 `LogConfigurationTests` 的全部边界（缺省/缺字段/解析/非法值/损坏 JSON）；既有 `settings.json` 原样生效，零迁移。
- **[架构测试表达力]** "只有 `SettingsService` 直接读写文件"难以用 NetArchTest 精确断言（文件路径常量难泛化匹配） -> 退而断言可表达的不变量：`ISettingsService` 在 Core、`SettingsService` 在 App；stores/`LogConfiguration` 依赖 `ISettingsService` 且不依赖被删的 `LocalAppDataSettingsFile` 类型（删除后引用即编译失败，天然强制）。
- **[删除静态助手的波及面]** `LocalAppDataSettingsFile` 被 stores 与测试引用 -> 删除前已把所有引用迁至 `ISettingsService`；编译器会捕获遗漏引用，`TreatWarningsAsErrors` + 全量构建兜底。

## Migration Plan

- 纯实现层重构，无数据/持久化迁移：`settings.json` 的键（`Language`/`Theme`/`MinimumLogLevel`）、文件位置、回退规则均不变，既有文件原样继续生效。
- 实现后运行 `dotnet build`（`TreatWarningsAsErrors`）与全部测试（含架构测试与新增 `SettingsServiceTests`、改写的 `LogConfigurationTests` / store 测试）。
- 回滚：直接 revert 本变更；`settings.json` 字段与格式不变、无 schema 残留，旧代码忽略新增的 `ISettingsService` 无害。
- 不依赖运行期数据迁移脚本；新增/改写测试随实现一并提交。

## Open Questions

- （主要决议均已落实：端口归属见 D1、端口形态见 D2、缓存策略与外部编辑权衡见 D3/Risks、stores 去留见 D4、`LogConfiguration` 重构见 D5。暂无遗留。）
