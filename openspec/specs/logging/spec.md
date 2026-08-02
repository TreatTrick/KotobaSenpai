# logging Specification

## Purpose
TBD - created by archiving change add-logging-system. Update Purpose after archive.
## Requirements
### Requirement: Logging port and implementation residence

Core SHALL define an `ILogger` port and a `LogLevel` enumeration as the logging abstraction. The concrete file-logging implementation SHALL reside in the App layer. Core and Platform.Windows SHALL NOT depend on the file-logging implementation. The `ILogger` port SHALL NOT reference `Core.Localization`, keeping the logging and localization cross-cutting ports decoupled.

#### Scenario: Port resides in Core, implementation in App

- **WHEN** the dependency direction is checked by the architecture tests
- **THEN** `ILogger` and `LogLevel` MUST reside in `KotobaSenpai.Core` and the file-logging implementation MUST reside in `KotobaSenpai.App`; ViewModels MUST depend only on the Core `ILogger` interface, never on the App implementation.

#### Scenario: Port is decoupled from localization

- **WHEN** the `ILogger` port is inspected
- **THEN** it MUST NOT reference `IUserFacingException`, `ErrorCodes`, or any `Core.Localization` type; extraction of `ErrorCode` from exceptions MUST occur in the App implementation, not in the Core port.

### Requirement: Error logging at error boundaries

The system SHALL log every handled error at the point where it is caught and turned into a user-facing outcome. ViewModels SHALL obtain an `ILogger` via constructor injection and SHALL log the exception (with type, message, and full stack trace) at the `Error` level in the single error-funnel method that all `catch` blocks route through. Throw sites in Core and Platform SHALL NOT log separately, because exceptions propagate intact to the catch boundary.

#### Scenario: Handled exception is logged with full detail

- **WHEN** an exception is caught in a ViewModel catch block and routed to the error-funnel method
- **THEN** the logger MUST record an `Error`-level entry containing the exception type, message, and full stack trace, in addition to a short human-readable context message.

#### Scenario: User-facing error code is attached to the log entry

- **WHEN** the caught exception implements `IUserFacingException`
- **THEN** the log entry MUST include its `ErrorCode` (e.g., `(ErrorCode=OcrLanguagePackMissing)`) automatically, without the caller passing the code explicitly.

#### Scenario: Non-coded exception uses fallback context

- **WHEN** the caught exception does not implement `IUserFacingException`
- **THEN** the log entry MUST still record the exception type, message, and stack trace, with no `ErrorCode` field, so the failure is still diagnosable.

#### Scenario: Errors are not double-logged

- **WHEN** an exception is thrown in Core or Platform and propagates to a ViewModel catch block
- **THEN** it MUST be logged exactly once (at the catch boundary), not at both the throw site and the catch site.

### Requirement: Daily rolling log files

The system SHALL write logs to one file per local calendar day, named `kotobasenpai-yyyy-MM-dd.log`, under `%LocalAppData%/KotobaSenpai/logs/`. When a log entry is written on a date different from the currently open file's date, the logger SHALL roll to a new file for the new date. Each log line SHALL begin with an ISO-8601 local timestamp with timezone offset, followed by the level, the optional `ErrorCode`, and the message; exception dumps SHALL follow as subsequent lines.

#### Scenario: One file per local date

- **WHEN** log entries are written across two consecutive local calendar days
- **THEN** the system MUST create two separate files named with the respective `yyyy-MM-dd` dates, not append both days to one file.

#### Scenario: Log line format

- **WHEN** an `Error`-level entry with `ErrorCode=OcrLanguagePackMissing` and context message "Recognition failed" is written
- **THEN** the line MUST begin with an ISO-8601 timestamp with offset, contain `[ERR]`, contain `(ErrorCode=OcrLanguagePackMissing)`, and be followed by the exception type, message, and stack trace on subsequent lines.

#### Scenario: Log directory is auto-created

- **WHEN** the application starts and `%LocalAppData%/KotobaSenpai/logs/` does not exist
- **THEN** the system MUST create the directory before writing the first log entry, rather than failing.

### Requirement: English-only log content

All log output SHALL be in English. Log messages, exception messages, stack traces, and error codes SHALL be recorded as-is in their original (English) form; the system SHALL NOT localize log content.

#### Scenario: Log content is English

- **WHEN** any log entry is written
- **THEN** the timestamp, level, error code, context message, and exception dump MUST be in English / locale-independent form, regardless of the active UI culture.

#### Scenario: Log content is independent of UI culture

- **WHEN** the active UI culture is `zh-CN` and an error is logged
- **THEN** the log entry MUST still be in English, not localized to Simplified Chinese, even though the user-facing status message shown in the UI is localized.

### Requirement: Configurable minimum log level

The file logger SHALL filter out entries below a configurable minimum log level. The default minimum level SHALL be `Error`. The minimum level SHALL be configurable via the user settings file (`%LocalAppData%/KotobaSenpai/settings.json`, field `MinimumLogLevel`); an absent or unparseable value SHALL fall back to `Error`.

#### Scenario: Default level is Error

- **WHEN** the application starts with no `MinimumLogLevel` configured
- **THEN** entries below `Error` (e.g., `Warning`, `Information`) MUST NOT be written to the log file, while `Error` and `Critical` entries MUST be written.

#### Scenario: Level is configurable

- **WHEN** `MinimumLogLevel` is set to `Warning` in the settings file
- **THEN** `Warning` and above MUST be written to the log file.

#### Scenario: Invalid value falls back to Error

- **WHEN** `MinimumLogLevel` is set to a value that cannot be parsed as a `LogLevel`
- **THEN** the logger MUST use `Error` as the minimum level and MUST NOT crash.

### Requirement: Seven-day retention with automatic cleanup

The system SHALL retain only log files from the most recent 7 days and SHALL automatically delete log files whose date is older than 7 days. Cleanup SHALL run at application startup and when the logger rolls to a new day's file.

#### Scenario: Expired files are deleted on startup

- **WHEN** the application starts and the logs directory contains a file whose date is older than 7 days
- **THEN** the system MUST delete that file during startup cleanup.

#### Scenario: Recent files are retained

- **WHEN** cleanup runs and the directory contains files dated within the last 7 days
- **THEN** those files MUST NOT be deleted.

#### Scenario: Seven-day boundary

- **WHEN** cleanup runs and a file's date is exactly 7 days old
- **THEN** the file MUST be retained; a file 8 days old MUST be deleted.

#### Scenario: Non-log files are not deleted

- **WHEN** cleanup runs and the directory contains a file whose name does not match the `kotobasenpai-yyyy-MM-dd.log` pattern
- **THEN** the system MUST NOT delete that file, regardless of its age.

#### Scenario: Day-roll cleanup deletes expired files during a session

- **WHEN** the logger rolls to a new day's file during a running session and the logs directory contains a file older than 7 days
- **THEN** the day-roll cleanup MUST delete the expired file without waiting for an application restart.

### Requirement: Global unhandled exception capture with user notice

The application SHALL register handlers for `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`, and each handler SHALL log the exception at the `Error` level with full detail and flush the entry to disk. For an unhandled exception that terminates the application, the handler SHALL, after logging, display a generic localized error notice to the user before the application exits. `DispatcherUnhandledException` SHALL set `Handled = true` and shut down the application after the user acknowledges the notice. `TaskScheduler.UnobservedTaskException` (non-terminating by default) SHALL log only, without a notice and without calling `SetObserved`.

#### Scenario: Unhandled UI exception logs, notifies, then exits

- **WHEN** an unhandled exception occurs on the WPF dispatcher UI thread
- **THEN** the `DispatcherUnhandledException` handler MUST log it at `Error` level with type, message, and stack trace, flush the entry, display a generic localized error notice, set `Handled = true`, and shut down the application after the user acknowledges.

#### Scenario: The notice is localized

- **WHEN** the generic error notice is displayed to the user
- **THEN** its text MUST be obtained via `IStringLocalizer` (Simplified Chinese or English per the active culture), not a hard-coded literal; the exception's stack trace MUST NOT be shown to the user.

#### Scenario: Log is flushed before exit

- **WHEN** the application exits via the unhandled-exception path
- **THEN** the crash log entry MUST be flushed to disk before the process terminates, so the failure is diagnosable.

#### Scenario: Background thread terminating exception is logged and noticed

- **WHEN** an unhandled terminating exception occurs on a non-UI thread (caught by `AppDomain.UnhandledException` with `IsTerminating == true`)
- **THEN** the handler MUST log it at `Error` level and flush; it SHOULD display the localized notice via the UI dispatcher when safely reachable.

#### Scenario: Non-terminating unobserved task exception is logged only

- **WHEN** a task exception is caught by `TaskScheduler.UnobservedTaskException`
- **THEN** the handler MUST log it at `Error` level and MUST NOT display a notice, MUST NOT call `SetObserved()`, and MUST NOT crash the application.

### Requirement: Thread-safe and durable logging

The file logger SHALL be safe for concurrent use from multiple threads (e.g., UI thread and thread-pool OCR work). The logger SHALL flush its buffer on application exit so entries written before a crash or shutdown are persisted. The logger SHALL NOT throw exceptions that propagate to callers when the underlying file system operation fails; such failures SHALL be swallowed so that logging never crashes the application.

#### Scenario: Concurrent writes are serialized

- **WHEN** two threads write log entries at the same time
- **THEN** both entries MUST be written intact without corruption or interleaving, serialized by an internal lock.

#### Scenario: Buffer is flushed on exit

- **WHEN** the application exits normally
- **THEN** the logger MUST flush and close the file handle so all entries written during the session are persisted to disk.

#### Scenario: Logging failures do not crash the app

- **WHEN** a file system error occurs while writing a log entry (e.g., disk full or access denied)
- **THEN** the logger MUST NOT throw an exception to the caller; the application MUST continue operating.
