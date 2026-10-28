# settings Specification

## Purpose
TBD - created by archiving change add-settings-service. Update Purpose after archive.
## Requirements
### Requirement: Settings service port and implementation residence
Core SHALL 定义 `ISettingsService` 作为设置读写的唯一端口，保持字符串键值与领域类型无关。文件实现 SHALL 位于 Platform.Windows 基础设施层，由 App 与 CLI 各自在入口组装并复用；Core SHALL 不依赖该文件实现。ViewModel 和功能设置适配 SHALL 仅依赖 Core 端口。

#### Scenario: Port resides in Core, implementation in infrastructure
- **WHEN** 架构测试检查设置依赖方向
- **THEN** `ISettingsService` 位于 `KotobaSenpai.Core`，文件实现位于 `KotobaSenpai.Platform.Windows`；UI 和 CLI 入口可以创建该实现，ViewModel 与功能设置适配只依赖接口

#### Scenario: Port is free of feature-specific types
- **WHEN** 检查 `ISettingsService`
- **THEN** 仅暴露字符串键值及其快照、条件批量操作，不引用主题、日志、本地化或其他功能专属类型

### Requirement: Single-owner file access

The `SettingsService` SHALL be the sole owner of `%LocalAppData%/KotobaSenpai/settings.json`. No feature, store, or configuration helper SHALL read or write the settings file directly; all settings access SHALL flow through the `ISettingsService` port. The static helper that previously read/wrote the file SHALL be removed.

#### Scenario: Features access settings only through the port

- **WHEN** the language preference store, the theme preference store, or the log-level configuration reads or writes a setting
- **THEN** it MUST do so via `ISettingsService`; it MUST NOT reference the settings file path, `File.Exists`, `JsonDocument`/`JsonNode.Parse`, or any removed static settings-file helper.

### Requirement: Preserve unknown fields

A write to one setting key SHALL NOT erase or alter other keys. The `Language`, `Theme`, and `MinimumLogLevel` fields SHALL coexist in the same file; changing one preference (e.g., switching language) MUST NOT loss the others (e.g., theme or log level).

#### Scenario: Writing one key preserves the others

- **WHEN** the file contains `{"Language":"zh-CN","Theme":"Dark","MinimumLogLevel":"Warning"}` and the user switches language so that `SetValue("Language", "en")` is called
- **THEN** the persisted file MUST still contain `Theme: "Dark"` and `MinimumLogLevel: "Warning"` unchanged, with only `Language` updated to `"en"`.

#### Scenario: Setting a new key preserves existing keys

- **WHEN** the file contains `{"Language":"zh-CN"}` and `SetValue("Theme", "Auto")` is called
- **THEN** the persisted file MUST contain both `Language: "zh-CN"` and `Theme: "Auto"`.

### Requirement: Tolerant of missing or corrupt file

The `SettingsService` SHALL NOT throw when the settings file is absent or contains unparseable JSON. A missing file SHALL be treated as an empty settings object. A corrupt (unparseable) file SHALL be treated as an empty settings object rather than crashing the application or propagating an exception to callers.

#### Scenario: Missing file behaves as empty settings

- **WHEN** `GetValue("Language")` is called and `settings.json` does not exist
- **THEN** the service MUST return `null` (no stored value) and MUST NOT throw.

#### Scenario: Corrupt JSON behaves as empty settings

- **WHEN** `GetValue("Language")` is called and `settings.json` contains unparseable content (e.g., `{not valid json`)
- **THEN** the service MUST return `null` and MUST NOT throw; a subsequent `SetValue` MUST write a valid file replacing the corrupt content.

#### Scenario: Absent key returns null

- **WHEN** `GetValue("Theme")` is called and the file contains `{"Language":"zh-CN"}` with no `Theme` key
- **THEN** the service MUST return `null`.

### Requirement: Write-through persistence

A `SetValue` call SHALL immediately persist the change atomically to disk (auto-creating the directory if needed) and publish the new in-memory settings only after persistence succeeds, so the change survives an application restart. The `SettingsService` SHALL load the file only after acquiring the applicable process write lease and hold it as the single in-memory view for the lifetime of the writable singleton; read-only snapshots SHALL NOT later be reused for writing.

#### Scenario: SetValue is immediately durable

- **WHEN** `SetValue("Theme", "Dark")` is called and the process is restarted immediately after
- **THEN** a subsequent `GetValue("Theme")` MUST return `"Dark"`.

#### Scenario: Directory is auto-created

- **WHEN** `SetValue` is called and `%LocalAppData%/KotobaSenpai/` does not exist
- **THEN** the service MUST create the directory before writing, rather than failing.

#### Scenario: Repeated reads do not re-read the file

- **WHEN** `GetValue("Language")` is called twice in succession
- **THEN** the service MUST serve the second call from its in-memory view without re-reading the file, because it is the sole owner of the settings state.

### Requirement: Injectable and testable settings access

The `ISettingsService` port SHALL be injectable via dependency injection and SHALL be consumable by an in-memory fake in unit tests, so that feature stores and log-level configuration can be tested without touching disk. The `SettingsService` SHALL be registered as a singleton so that all consumers share the single in-memory owner of the settings file.

#### Scenario: Stores are testable with an in-memory fake

- **WHEN** a unit test constructs the language or theme preference store with a fake `ISettingsService` and calls `Save` then `Load`
- **THEN** the store MUST round-trip its value through the fake without any file I/O, and the existing parsing/defaulting behavior (e.g., invalid enum falls back to null, whitespace is ignored) MUST be preserved.

#### Scenario: Log-level configuration is testable with an in-memory fake

- **WHEN** a unit test calls log-level configuration with a fake `ISettingsService` that returns `"Warning"` / `null` / an unparseable string
- **THEN** the configuration MUST resolve to `Warning` / the `Error` default / the `Error` default respectively, without any file I/O.

#### Scenario: Singleton shared across consumers

- **WHEN** the language store, the theme store, and the log-level configuration are resolved from the DI container during a single application run
- **THEN** they MUST all receive the same `SettingsService` singleton instance, so there is exactly one in-memory owner of the settings file.

### Requirement: 设置服务中的自动识别开关

系统 SHALL 在 `ISettingsService` 中支持 `AutoRecognitionEnabled` 布尔设置项，用于控制自动轮询识别功能的开关状态。该设置项 SHALL 遵循与既有设置相同的存储和读写约定：通过 `GetValue`/`SetValue` 以字符串形式读写（值 `"true"` 表示启用，其他值或缺省表示禁用）。

#### Scenario: 读取自动识别设置
- **WHEN** 调用方读取 `AutoRecognitionEnabled` 设置
- **THEN** 已存储的字符串原样返回；未存储时返回 null，SHALL NOT 抛出异常；调用方仅将 `"true"` 解释为启用，其他值或缺省均解释为禁用

#### Scenario: 写入自动识别设置
- **WHEN** 调用方写入 `AutoRecognitionEnabled = "true"`
- **THEN** 设置值持久化到 settings.json，后续读取 SHALL 返回 `"true"`

#### Scenario: 未设置时返回默认值
- **WHEN** 用户从未设置过 `AutoRecognitionEnabled`
- **THEN** 读取返回 null，系统 SHALL 默认禁用自动识别

### Requirement: 自动识别设置开关持久化
`AutoRecognitionEnabled` 设置项 SHALL 遵循现有 settings 服务的持久化约束：与其他设置共存于同一文件，写入时 SHALL NOT 擦除其他键。

#### Scenario: 写入自动识别不擦除其他设置
- **WHEN** settings.json 中包含 `{"Language":"zh-CN","Theme":"Dark"}`，设置 `AutoRecognitionEnabled` 为 `"true"`
- **THEN** 持久化后的文件 SHALL 同时包含 `Language`、`Theme` 和 `AutoRecognitionEnabled`

### Requirement: 原子字符串键值快照与条件批量提交
`ISettingsService` SHALL 提供领域类型无关的字符串键值批量快照和条件批量提交能力，空值继续表示删除键。接口 SHALL 仅声明契约，由具体实现使用私有同步机制保证一致性。条件提交 SHALL 在同一临界区比较指定相关键并应用全部变更。文件 SHALL 只由 Platform.Windows 基础设施层的共用 `SettingsService` 实现访问，UI 与 CLI SHALL 复用该实现。单键和批量保存均 SHALL 通过同目录临时文件与原子替换持久化，成功后再发布新内存状态；失败 SHALL 保留原内存和文件。无关设置与未知字段 SHALL 保留。本变更 SHALL 不要求通用设置变更事件或跨进程文件监听，CLI 写入后的界面通过重新启动读取最终值。

#### Scenario: 整体读取多个键
- **WHEN** 另一个操作正在替换完整 LLM 配置
- **THEN** 一次批量读取只得到替换前或替换后的完整快照，不混合两者的密钥、地址或模型

#### Scenario: 保存失败
- **WHEN** 临时文件写入或最终替换失败
- **THEN** 原配置文件仍有效，内存仍为原值，不返回保存成功

#### Scenario: 相关键发生冲突
- **WHEN** 条件提交发现任一预期 LLM 键已被改变
- **THEN** 整批变更均不应用，并返回冲突

#### Scenario: 无关偏好发生变化
- **WHEN** UI 内部检测期间只修改语言或主题，而 LLM 相关键仍匹配
- **THEN** 允许提交，并保留最新的语言、主题及未知字段

### Requirement: 跨进程独占写入与只读快照
GUI 与 CLI SHALL 共用按 Windows 用户区分的进程互斥，在创建可写设置实例和加载缓存之前取得；GUI 持有到退出，CLI 持有到配置操作结束。Mutex SHALL 由取得它的同一线程释放，包括异步配置路径。无法取得写入权 SHALL 有界返回忙碌，不加载可写旧缓存、不修改文件。只读命令 SHALL 允许在 GUI 运行时通过基础设施设置服务读取完整文件快照，但不保存任何设置、不依赖其他启动副作用写入，也不将此缓存用于后续写入。CLI SHALL 不自行实现文件读写或 JSON 持久化。

#### Scenario: 配置进程加载最新值
- **WHEN** GUI 正常退出后，CLI 取得写入互斥
- **THEN** 再由现有设置服务加载完整最新文件，不采用关闭 GUI 前读取的缓存

#### Scenario: 已有进程正在写入
- **WHEN** 另一个 GUI 或 CLI 已持有写入互斥
- **THEN** 新写入者返回忙碌，不产生同时拥有设置缓存的两个写入进程

#### Scenario: GUI 写入时查询状态
- **WHEN** 只读状态查询与 GUI 的单键或批量保存同时发生
- **THEN** 查询只读取原子替换前或替换后的完整 JSON，不观察半份文件、不写回读取的缓存

#### Scenario: 配置后重新打开设置页
- **WHEN** CLI 完成操作并释放互斥，GUI 再次启动
- **THEN** 新设置服务和 ViewModel 从最终持久化配置初始化，不需要外部变更通知或文件监听
