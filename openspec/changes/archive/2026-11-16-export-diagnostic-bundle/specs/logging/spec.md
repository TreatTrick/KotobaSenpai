## MODIFIED Requirements

### Requirement: Configurable minimum log level

文件日志器 SHALL 丢弃低于可配置最低级别的日志。默认最低级别 SHALL 为 `Debug`。最低级别 SHALL 通过用户设置文件（`%LocalAppData%/KotobaSenpai/settings.json` 的 `MinimumLogLevel` 字段）配置；字段缺失或无法解析时 SHALL 回退到 `Debug`。

#### Scenario: 默认级别为 Debug

- **WHEN** 应用启动时没有配置 `MinimumLogLevel`
- **THEN** `Trace` 日志 SHALL NOT 写入，`Debug`、`Information`、`Warning`、`Error` 和 `Critical` 日志 SHALL 写入

#### Scenario: 使用用户配置的级别

- **WHEN** `MinimumLogLevel` 在设置文件中设为 `Warning`
- **THEN** `Warning` 及以上级别 SHALL 写入

#### Scenario: 无效值回退为 Debug

- **WHEN** `MinimumLogLevel` 无法解析为 `LogLevel`
- **THEN** 日志器 SHALL 使用 `Debug` 且不得崩溃
