## 1. Core logging port

- [x] 1.1 Add `Core/Logging/LogLevel.cs` enum: `Trace, Debug, Information, Warning, Error, Critical`.
- [x] 1.2 Add `Core/Logging/ILogger.cs` port with `void Log(LogLevel level, Exception? exception, string message, params object[] args)` plus convenience `LogError(Exception, string, params object[])`, `LogWarning(string, params object[])`, `LogInformation(string, params object[])`. The port MUST NOT reference `Core.Localization` (`IUserFacingException`/`ErrorCodes`).

## 2. App file logger implementation

- [x] 2.1 Add `App/Logging/FileLogger.cs` implementing `ILogger`. Writes to `%LocalAppData%/KotobaSenpai/logs/kotobasenpai-yyyy-MM-dd.log` (auto-create directory). Holds the current day's `StreamWriter` and an internal `lock` serializing all writes.
- [x] 2.2 Implement daily rolling: before each write, compare `DateTime.Today` to the open file's date; on mismatch, close the old handle, open the new file, and trigger `LogRetentionPolicy.Cleanup` (day-roll trigger).
- [x] 2.3 Implement line formatting: ISO-8601 local timestamp with offset, level tag (`[ERR]`/`[WRN]`/`[INF]`), optional `(ErrorCode=…)` when `exception is IUserFacingException`, the formatted message, then the exception dump (type + message + full `StackTrace`) on subsequent lines.
- [x] 2.4 Make `FileLogger` resilient: swallow all `IOException`/unauthorized-access failures from the file system so logging never throws to callers. Implement `IDisposable` with a synchronous flush+close on `Dispose`.
- [x] 2.5 Add `App/Logging/LogRetentionPolicy.cs`: `Cleanup()` scans the logs directory, parses the date from filenames matching `kotobasenpai-yyyy-MM-dd.log` (fall back to `LastWriteTime`), and deletes files older than 7 days. MUST NOT delete non-matching files. (Exactly-7-days retained; 8-days deleted.)
- [x] 2.6 Add `App/Logging/LogConfiguration.cs`: read optional `MinimumLogLevel` (string) from `%LocalAppData%/KotobaSenpai/settings.json` via BCL `System.Text.Json` (tolerant of absent file/field), parse to `LogLevel`, default to `Error` on absent/unparseable. Inject into `FileLogger`.
- [x] 2.7 In `FileLogger`, filter entries below the configured minimum level before formatting/writing (e.g., `LogWarning`/`LogInformation` dropped when min level is `Error`); `Error`/`Critical` always pass the default.

## 3. Wire ViewModel and startup

- [x] 3.1 Update `MainWindowViewModel`: add `ILogger` to the constructor (injected after existing dependencies), store it, and in `SetError(Exception, string fallbackErrorCode)` call `_logger.LogError(exception, "<context>")` before resolving the user message -- so every error path is logged once at the catch boundary. Do NOT log in the individual `catch` blocks (funnel through `SetError`).
- [x] 3.2 Update `App.xaml.cs ConfigureServices`: register `FileLogger` as a singleton and bind `ILogger` to it (`services.AddSingleton<FileLogger>(); services.AddSingleton<ILogger>(sp => sp.GetRequiredService<FileLogger>());`).
- [x] 3.3 Update `App.xaml.cs OnStartup`: after building the provider, run `LogRetentionPolicy.Cleanup` once (startup trigger); store the `ILogger`/`FileLogger` on an instance field for use by global handlers. (No background timer; runtime cleanup is event-driven via day-roll in `FileLogger`.)
- [x] 3.4 Update `App.xaml.cs OnStartup`: register `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`. Each logs at `Error` via the stored `ILogger` and flushes. For terminating handlers, after logging+flush, display a generic localized error notice (resolve `IStringLocalizer` from the container; tolerate null during very-early startup -- then skip the notice and just log) via `MessageBox`/Dispatcher, then exit: `DispatcherUnhandledException` sets `Handled = true` and calls `Shutdown(1)` after the user acknowledges; `AppDomain.UnhandledException` (`IsTerminating == true`) shows the notice via `Application.Current.Dispatcher.Invoke` when safely reachable, then lets the process terminate. `TaskScheduler.UnobservedTaskException` logs only (no notice, no `SetObserved`).
- [x] 3.5 Update `App.xaml.cs OnExit`: dispose the `FileLogger` (flush+close).
- [x] 3.6 Add localization resource keys for the crash notice (e.g., `UnexpectedError_Title`, `UnexpectedError_Message`) to `App/Resources/Strings.resx` (English) and `Strings.zh-CN.resx` (Simplified Chinese); reference them from the global handlers via `IStringLocalizer`. (Follows the `add-i18n` key-coverage test convention.)

## 4. Tests and architecture

- [x] 4.1 Add `KotobaSenpai.App.Tests/FileLoggerTests`: log entry produces a line with ISO-8601 timestamp + level + message; `IUserFacingException` exceptions produce `(ErrorCode=…)`; exception dump includes type, message, and stack; writes across two dates create two files (rolling); directory auto-created; concurrent writes are serialized (no corruption); file-system failure does not throw to caller. Use a temp directory and inject a clock/date override for the day-roll test.
- [x] 4.2 Add `KotobaSenpai.App.Tests/LogRetentionPolicyTests`: file 8 days old is deleted; file exactly 7 days old is retained; recent files retained; non-matching filename is NOT deleted; date parsed from filename, `LastWriteTime` fallback covered. Use temp directory.
- [x] 4.3 Extend `KotobaSenpai.Architecture.Tests/DependencyDirectionTests`: assert `ILogger` and `LogLevel` reside in `KotobaSenpai.Core` and `FileLogger` resides in `KotobaSenpai.App`; assert ViewModels depend only on the Core `ILogger` interface, not on `FileLogger`; assert `ILogger` assembly does not depend on `Core.Localization` (or that the port type's assembly is Core and the port references no localization type).
- [x] 4.4 Update `MainWindowViewModelTests` (and any ViewModel test constructing `MainWindowViewModel`): supply a fake/null `ILogger` (e.g., a capturing fake or a no-op `NullLogger`) in the constructor so existing tests compile and pass; assert that a caught exception triggers one `LogError` call.
- [x] 4.5 Run `dotnet build` (with `TreatWarningsAsErrors`) and `dotnet test` for all projects including architecture tests; everything passes.
- [x] 4.6 Add tests for `FileLogger` level filtering: default min level `Error` drops `Warning`/`Information` and keeps `Error`/`Critical`; `MinimumLogLevel=Warning` allows `Warning`; an unparseable `MinimumLogLevel` falls back to `Error` without crashing. Cover via an injectable min-level/config override in a temp directory.

## 5. Verification

- [x] 5.1 Manual smoke: trigger each error path (recognize with no window / OCR language pack missing / window enumeration failure) and confirm an English `Error`-level entry with `ErrorCode`, exception type, message, and stack appears in `%LocalAppData%/KotobaSenpai/logs/kotobasenpai-yyyy-MM-dd.log`.
- [x] 5.2 Manual smoke: place (or age) a log file dated 8+ days old in the logs directory; launch the app and confirm it is deleted on startup, while a 7-day-old file is retained.
- [x] 5.3 Manual smoke: keep the app running across local midnight; confirm a new dated log file is created and the day-roll cleanup removes expired files without a restart.
- [x] 5.4 Manual smoke: confirm the UI culture switch (`zh-CN` <-> `en`) does not change log content -- logs remain English while the UI status text localizes.
- [x] 5.5 Manual smoke: force an unhandled UI-thread exception (e.g., throw inside a command); confirm the generic localized error notice appears (in `zh-CN` and `en` per active culture, no stack trace shown to the user), the entry is flushed to the log file, and the app exits after acknowledging -- with no second Windows Error Reporting dialog. Set `MinimumLogLevel=Warning` in `settings.json` and confirm lower-level entries appear; remove it and confirm only `Error+` is logged.
