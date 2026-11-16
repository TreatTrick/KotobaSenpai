# diagnostic-bundle-export Specification

## Purpose

Define the local diagnostic ZIP export flow and its confirmation, content scope, and failure behavior.

## Requirements

### Requirement: User confirms a local diagnostic bundle

The settings page SHALL provide a diagnostic bundle export action. Before creating a ZIP, the application SHALL show a prominent confirmation dialog that lists the actual log and diagnostic file categories and counts collected for this export. The dialog SHALL state that the ZIP is created locally and is not uploaded automatically. Cancelling the confirmation or the save-location picker SHALL NOT create a ZIP.

#### Scenario: Confirm diagnostic bundle contents

- **WHEN** the user starts an export from settings and files are available
- **THEN** the application shows the actual file categories and counts before creating the ZIP and asks whether to continue

#### Scenario: Cancel export

- **WHEN** the user cancels the confirmation or cancels the ZIP save-location picker
- **THEN** the application does not create a ZIP

#### Scenario: Only logs are available

- **WHEN** log files exist but no diagnostic files are available
- **THEN** the application still allows export and states that no diagnostic files were found

#### Scenario: Confirmation dialog follows the FluentLight theme

- **WHEN** the confirmation dialog is shown as a separate window
- **THEN** it uses the current application light or dark theme, provides its own `FluentLightHost`, and configures FluentLight surfaces and controls with corner radii matching their rendered corners

### Requirement: Diagnostic bundles include only allowed local files

The application SHALL add existing log files that match `kotobasenpai-yyyy-MM-dd.log` and known diagnostic files written by the application to the ZIP, preserving their file names under `logs/` and `diag/` respectively. Diagnostic files SHALL use one of these prefixes: `frame-`, `ocr-`, `tokens-`, `phrase-`, `llm-req-`, `llm-resp-`, `llm-groups-`, or `llm-words-`. The application SHALL NOT add settings files, credentials, or other files that do not match these rules.

#### Scenario: Include existing log and diagnostic files

- **WHEN** matching files exist in the log and diagnostic directories
- **THEN** the ZIP contains them under `logs/` and `diag/`, preserving their original file names

#### Scenario: Exclude settings and unknown files

- **WHEN** settings files, credentials, or files with unrecognized names exist in the relevant directories
- **THEN** the ZIP excludes those files

#### Scenario: Export fails

- **WHEN** the destination cannot be written or a source file cannot be read
- **THEN** the application shows a localized failure message and does not report a successful export
