## Why

应用的用户设置目前散落在多处各自读写 `%LocalAppData%/KotobaSenpai/settings.json`：`LocalAppDataSettingsFile`（静态助手）、`LocalAppDataLanguagePreferenceStore`、`LocalAppDataThemePreferenceStore` 都经该助手做读-改-写，而 `LogConfiguration` 甚至绕过助手、用自带的 `DefaultPath` + `JsonDocument.Parse` 重复实现了一遍文件读取逻辑。结果是每个功能都要自己处理文件 I/O、目录创建、损坏 JSON 容错，逻辑重复且分散；静态助手既不可注入也不可 mock，还被 awkwardly 放在 `Localization` 命名空间下。`ILanguagePreferenceStore`/`IThemePreferenceStore` 的注释早已写明"待设置模块落地后迁移"。本变更把设置信息的读写收拢到一个可注入的统一服务，消除重复、使设置可测试，并为后续更多设置项建立单一归属。

## What Changes

- 新增 `ISettingsService` 端口（Core）与 `SettingsService` 实现（App）：作为 `settings.json` 的**唯一归属**，按字符串键读写设置值；保留未知字段（`Language`/`Theme`/`MinimumLogLevel` 共存互不覆盖）；文件缺失或损坏时视为空对象不抛异常；单例、懒加载到内存 + 写穿（write-through）持久化，内部 `lock` 串行化。
- 重构 `LogConfiguration`：改为经 `ISettingsService` 读取 `MinimumLogLevel` 字段，**删除其重复的文件读取逻辑**；`FileLogger` 的 DI 注册 lambda 解析 `ISettingsService` 取最小级别。
- 重构 `LocalAppDataLanguagePreferenceStore` / `LocalAppDataThemePreferenceStore`：文件 I/O 改为委托 `ISettingsService`，二者降为 `Language`/`Theme` 键之上的**薄类型化门面**（含既有 Enum/空白校验）；端口接口与消费者（`LanguageService`/`FluentThemeService`）不变。
- 删除静态助手 `LocalAppDataSettingsFile`（职责由 `SettingsService` 吸收）。
- 在 DI 容器注册 `ISettingsService` 为单例，并调整注册顺序使其先于 `FileLogger` 可解析。
- 新增架构测试：断言 `ISettingsService` 端口位于 Core、`SettingsService` 实现位于 App；断言 stores / `LogConfiguration` 不再直接读写 `settings.json`（依赖端口而非文件路径）。
- 新增 `settings` 能力 spec。

行为不变：语言偏好、主题模式、最小日志级别的持久化键与文件位置不变，缺省/非法值回退规则不变；既有 `settings.json` 原样继续生效。本变更为纯实现层收拢重构，不引入新 NuGet（沿用 BCL `System.Text.Json`），不引入设置 UI。

## Capabilities

### New Capabilities

- `settings`: 集中化用户设置服务--以单一服务拥有 `%LocalAppData%/KotobaSenpai/settings.json` 的读写，按字符串键取/存值，保留未知字段，容忍文件缺失与损坏，端口位于 Core、文件实现位于 App，可经 in-memory fake 测试。

### Modified Capabilities

无。`localization` 的"语言偏好持久化"与 `logging` 的"可配置最小日志级别"需求行为不变（仍写入同一 `settings.json` 的同一字段、同样回退默认值）；本变更只把"如何读写文件"从各功能收拢到统一服务，属实现细节重构，不改 spec 级需求。

## Impact

- **新增文件**：`Core/Settings/ISettingsService.cs`（端口）；`App/Settings/SettingsService.cs`（文件实现，吸收 `LocalAppDataSettingsFile` 职责）；`KotobaSenpai.App.Tests/SettingsServiceTests.cs`（保留未知字段、缺失/损坏容错、写穿、并发串行化）。
- **修改文件**：`App/Logging/LogConfiguration.cs`（改为接收 `ISettingsService` 读 `MinimumLogLevel`，删除自带文件逻辑）；`App/Localization/LocalAppDataLanguagePreferenceStore.cs` 与 `LocalAppDataThemePreferenceStore.cs`（委托 `ISettingsService`）；`App/App.xaml.cs`（注册 `ISettingsService` 单例、`FileLogger` 经其取最小级别）；`KotobaSenpai.Architecture.Tests/DependencyDirectionTests.cs`（端口/实现归属 + 唯一文件归属断言）；`KotobaSenpai.App.Tests/LogConfigurationTests.cs` 及 store 相关测试（改用 in-memory fake `ISettingsService`）。
- **删除文件**：`App/Localization/LocalAppDataSettingsFile.cs`（职责并入 `SettingsService`）。
- **依赖**：不引入新 NuGet（保持 Core 零外部依赖、BCL-only）；设置读写沿用 `System.Text.Json` / `System.IO`。
- **架构**：不破坏依赖方向--`ISettingsService` 端口位于 Core，`SettingsService` 实现位于 App；ViewModel 不依赖设置实现；stores/`LogConfiguration` 依赖 Core 端口而非文件路径。
