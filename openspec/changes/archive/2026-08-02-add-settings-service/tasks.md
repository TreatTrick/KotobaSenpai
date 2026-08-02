## 1. Core settings port

- [x] 1.1 Add `Core/Settings/ISettingsService.cs` port with `string? GetValue(string key)` and `void SetValue(string key, string? value)`. The port MUST be domain-type-free (no reference to `AppThemeMode`, `LogLevel`, `CultureInfo`, or any feature type) and MUST NOT reference `System.Text.Json`/`System.IO` (pure abstraction).
- [x] 1.2 Confirm `KotobaSenpai.Core` still has zero `PackageReference` after adding the port (BCL-only).

## 2. App settings service implementation

- [x] 2.1 Add `App/Settings/SettingsService.cs` implementing `ISettingsService`. Hold the settings file path constant (`%LocalAppData%/KotobaSenpai/settings.json`, migrated from `LocalAppDataSettingsFile`). Allow injecting an override file path in the constructor for testing (default to the real path).
- [x] 2.2 Implement lazy in-memory load: on first `GetValue`/`SetValue`, read the file into a `JsonObject` (use `System.Text.Json.Nodes`); if the file is missing or unparseable (`IOException`/`JsonException`), treat as an empty `JsonObject`. MUST NOT throw to callers.
- [x] 2.3 Implement `GetValue`: read the key from the in-memory `JsonObject`; return `null` when the key is absent or its value is null. Serve from memory on subsequent calls (no file re-read).
- [x] 2.4 Implement `SetValue`: update the in-memory `JsonObject` key and immediately write through to disk (auto-create the directory; `WriteIndented`), all under an internal `lock` serializing all reads/writes.
- [x] 2.5 Preserve unknown fields on write: serialize the full in-memory `JsonObject` (not just the changed key), so `Language`/`Theme`/`MinimumLogLevel` coexist.

## 3. Refactor LogConfiguration via the port

- [x] 3.1 Change `App/Logging/LogConfiguration.cs`: replace `LoadMinimumLevel(string? filePath)` with `LoadMinimumLevel(ISettingsService settings)` that calls `settings.GetValue("MinimumLogLevel")`, parses to `LogLevel` via `Enum.TryParse` (ignore case), and falls back to `LogLevel.Error` on null/unparseable. Delete `DefaultPath`, `File.Exists`, `JsonDocument.Parse`, and the `IOException`/`JsonException` catches (file I/O + tolerance now live in `SettingsService`).
- [x] 3.2 Update `App/App.xaml.cs` `FileLogger` registration lambda to resolve the port and pass the parsed level: `LogConfiguration.LoadMinimumLevel(sp.GetRequiredService<ISettingsService>())`.

## 4. Refactor preference stores via the port

- [x] 4.1 Refactor `App/Localization/LocalAppDataLanguagePreferenceStore.cs`: inject `ISettingsService` via constructor; `Load` calls `GetValue("Language")` (return null if absent/whitespace, preserving existing whitespace check); `Save` calls `SetValue("Language", cultureName)`. Remove direct use of `LocalAppDataSettingsFile`.
- [x] 4.2 Refactor `App/Localization/LocalAppDataThemePreferenceStore.cs`: inject `ISettingsService`; `Load` calls `GetValue("Theme")` and runs the existing `Enum.TryParse<AppThemeMode>` (return null on absent/unparseable); `Save` calls `SetValue("Theme", mode.ToString())`. Remove direct use of `LocalAppDataSettingsFile`.
- [x] 4.3 Leave `ILanguagePreferenceStore` / `IThemePreferenceStore` interfaces and their consumers (`LanguageService`, `FluentThemeService`) unchanged.

## 5. DI wiring and remove old helper

- [x] 5.1 In `App/App.xaml.cs ConfigureServices`, register `services.AddSingleton<ISettingsService, SettingsService>()` (place near the other singletons; MS.DI resolves it lazily when `FileLogger`/stores are constructed).
- [x] 5.2 Delete `App/Localization/LocalAppDataSettingsFile.cs`. Build to confirm no remaining references (compiler-enforced); migrate any stray reference to `ISettingsService`.

## 6. Tests

- [x] 6.1 Add `KotobaSenpai.App.Tests/SettingsServiceTests.cs` using a temp file path: missing file -> `GetValue` returns null, no throw; corrupt JSON -> returns null, no throw, then `SetValue` writes a valid file; absent key -> null; `SetValue` then `GetValue` round-trips; writing one key preserves the others (`Language`/`Theme`/`MinimumLogLevel` coexist); directory auto-created; repeated `GetValue` does not re-read the file (verify via a single load).
- [x] 6.2 Rewrite `KotobaSenpai.App.Tests/LogConfigurationTests.cs` to use an in-memory fake `ISettingsService`: `"Warning"` -> `LogLevel.Warning`; `null`/absent -> `Error`; unparseable string -> `Error`. Remove all temp-file/`WriteSettings` file I/O.
- [x] 6.3 Update/add store tests (e.g., in `LanguageServiceTests` or a dedicated store test) using a fake `ISettingsService`: language store round-trips `Language` and treats whitespace as null; theme store round-trips `Theme` and returns null for an unparseable enum; both preserve their existing defaulting behavior with no file I/O.
- [x] 6.4 Confirm existing `LanguageServiceTests` / `FluentThemeService`-related tests still pass with stores now delegating to a fake `ISettingsService` (inject the fake into the store, then the store into the service).

## 7. Architecture tests

- [x] 7.1 Extend `KotobaSenpai.Architecture.Tests/DependencyDirectionTests.cs`: assert `ISettingsService` resides in `KotobaSenpai.Core` and `SettingsService` resides in `KotobaSenpai.App` (mirror the `Localization_Port_ResidesInCore_Implementation_ResidesInApp` / `Logging_Port_ResidesInCore_Implementation_ResidesInApp` pattern).
- [x] 7.2 Add an architecture assertion that feature stores and `LogConfiguration` depend on `ISettingsService` (Core port) and do NOT depend on the removed `LocalAppDataSettingsFile` type (the delete makes this compile-enforced; assert ViewModels still do not depend on `KotobaSenpai.App.Settings`).

## 8. Verification

- [x] 8.1 Run `dotnet build` (with `TreatWarningsAsErrors`) and `dotnet test` for all projects including architecture tests; everything passes.
- [x] 8.2 Manual smoke: launch the app with an existing `settings.json` containing `Language`/`Theme`/`MinimumLogLevel`; confirm language restores, theme restores, and log level applies -- all fields preserved unchanged.
- [x] 8.3 Manual smoke: switch language at runtime, then switch theme, then restart; confirm both preferences persist and neither erased the other (and `MinimumLogLevel` still present).
- [x] 8.4 Manual smoke: delete `settings.json` and launch (no crash, defaults apply); corrupt the file with invalid JSON and launch (no crash, treated as empty); set `MinimumLogLevel=Warning` and confirm lower-level log entries appear.
