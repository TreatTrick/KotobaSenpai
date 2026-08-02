## ADDED Requirements

### Requirement: Settings service port and implementation residence

Core SHALL define an `ISettingsService` port as the single abstraction for reading and writing user settings. The concrete file-based implementation SHALL reside in the App layer. Core and Platform.Windows SHALL NOT depend on the file-based implementation. The `ISettingsService` port SHALL be domain-type-free: it SHALL NOT reference `AppThemeMode`, `LogLevel`, `CultureInfo`, or any feature-specific type.

#### Scenario: Port resides in Core, implementation in App

- **WHEN** the dependency direction is checked by the architecture tests
- **THEN** `ISettingsService` MUST reside in `KotobaSenpai.Core` and the file-based implementation MUST reside in `KotobaSenpai.App`; ViewModels and feature stores MUST depend only on the Core `ISettingsService` interface, never on the App implementation.

#### Scenario: Port is free of feature-specific types

- **WHEN** the `ISettingsService` port is inspected
- **THEN** it MUST expose only string-keyed, string-valued access (e.g., `GetValue(string key)` / `SetValue(string key, string? value)`); it MUST NOT reference theme, logging, or localization domain types, so that feature-specific parsing stays in the feature adapters.

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

A `SetValue` call SHALL update the in-memory settings and SHALL immediately persist the change to disk (auto-creating the directory if needed), so the change survives an application restart. The `SettingsService` SHALL lazily load the file into memory on first access and hold it as the single in-memory view for the lifetime of the singleton.

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
