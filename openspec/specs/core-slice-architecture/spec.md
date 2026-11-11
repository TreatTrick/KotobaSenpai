# Core Slice Architecture

## Purpose

Define the capability-slice organization and dependency rules for `KotobaSenpai.Core`.

## Requirements

### Requirement: Core is organized as capability slices

`KotobaSenpai.Core` SHALL be organized by business capability, not by technical type. Every Core type MUST reside in a namespace named after the capability it serves, and that namespace MUST contain the capability's value objects, domain services, and port interfaces together.

The namespaces `KotobaSenpai.Core.Models`, `KotobaSenpai.Core.Services`, and `KotobaSenpai.Core.Contracts` MUST NOT exist.

#### Scenario: No technical-type namespaces remain

- **WHEN** the architecture tests enumerate every type in the `KotobaSenpai.Core` assembly
- **THEN** no type SHALL reside in a namespace ending in `.Models`, `.Services`, or `.Contracts`.

#### Scenario: A port lives with the slice that consumes it

- **WHEN** a Core port interface such as `ITokenizer`, `IJmdictRepository`, or `IOverlayRenderer` is located
- **THEN** it MUST reside in the same namespace as the capability that consumes it, not in a separate contracts namespace.

#### Scenario: Adapter projects are unaffected

- **WHEN** the folder layout of `KotobaSenpai.Platform.Windows` and `KotobaSenpai.App` is inspected
- **THEN** they MAY keep technology-oriented folder names such as `Capture`, `Llm`, `Ocr`, `Themes`, and `ViewModels`, because adapter and composition-root layers are organized by technology rather than by domain capability.

### Requirement: Slice dependencies follow a strict layer order

每个 Core 能力必须（MUST）有明确的层号。它只能依赖层号更小的能力，不能依赖同层或更高层的能力。因此，同一层的能力必须互相独立，不能形成循环依赖。

分层必须（MUST）如下，与现有架构测试保持一致：

| 层号 | 能力 |
| --- | --- |
| 0 | `Geometry`、`Threading`、`Session`、`Japanese`、`Errors`、`Localization`、`Logging`、`Settings`、`Shortcuts` |
| 1 | `Llm`、`TargetWindow`、`Updates` |
| 2 | `Ocr` |
| 3 | `Tokenization` |
| 4 | `Dictionary` |
| 5 | `WordGrouping`、`PhraseAnalysis` |
| 6 | `Diagnostics`、`Overlay` |
| 7 | `Workflow` |

#### Scenario: 依赖更高层时测试失败
- **WHEN** 第 3 层的 `Tokenization` 依赖第 4 层的 `Dictionary`
- **THEN** 架构测试必须失败，并指出违规类型。

#### Scenario: 同层能力不能互相依赖
- **WHEN** 第 5 层的 `PhraseAnalysis` 依赖 `WordGrouping`，或者反过来依赖
- **THEN** 架构测试必须失败。

#### Scenario: 第零层没有对其他能力的依赖
- **WHEN** 检查第 0 层的任何能力
- **THEN** 它不得依赖其他 Core 能力。

#### Scenario: 工作流仍是原有领域流程的入口
- **WHEN** 检查 `Workflow` 之外的任何 Core 能力
- **THEN** 它不得依赖 `Core.Workflow`，避免领域反过来调用上层应用流程。

#### Scenario: 合法的向下依赖不需要修改测试
- **WHEN** 第 6 层的 `Overlay` 增加对第 2 层 `Ocr` 的依赖
- **THEN** 现有架构测试应直接通过，不需要放宽规则。

#### Scenario: 更新能力保持独立
- **WHEN** 检查新增的 `Core.Updates`
- **THEN** 它可以使用第 0 层的错误码，但不能依赖 `Llm`、`TargetWindow`、识别工作流、HTTP、文件系统或 WPF。

### Requirement: No general-purpose shared namespace

Core MUST NOT contain a namespace whose name signals an unclassified bucket. Shared types SHALL instead live in a namespace named after what they actually are.

#### Scenario: Bucket namespaces are rejected

- **WHEN** the architecture tests enumerate Core namespaces
- **THEN** no namespace segment SHALL be named `Shared`, `Common`, `Util`, `Utils`, `Helpers`, `Misc`, or `Infrastructure`.

#### Scenario: Shared types carry concrete names

- **WHEN** a type is shared across capabilities, such as `ScreenRect`, `Kana`, or `ErrorCodes`
- **THEN** it MUST reside in a layer-0 slice named for its subject matter (`Geometry`, `Japanese`, `Errors`), so that adding a file requires answering which subject it belongs to.

### Requirement: Namespace matches folder structure

Every source file's namespace SHALL match its folder path relative to the project root. This constraint MUST be enforced at compile time, not by review.

#### Scenario: A misplaced file fails the build

- **WHEN** a file located at `src/KotobaSenpai.Core/Overlay/Foo.cs` declares `namespace KotobaSenpai.Core.Ocr`
- **THEN** the build MUST fail with `IDE0130` reported as an error.

#### Scenario: The rule is configured repository-wide

- **WHEN** the repository root `.editorconfig` is inspected
- **THEN** it MUST set `dotnet_diagnostic.IDE0130.severity` to `error`.

### Requirement: Slice reorganization preserves behavior

Reorganizing Core into capability slices SHALL NOT change any runtime behavior, user-visible text, configuration key, or public contract consumed by the adapter and composition-root layers.

#### Scenario: The build stays clean

- **WHEN** the solution is built after the reorganization
- **THEN** it MUST report zero warnings and zero errors, matching the pre-change baseline.

#### Scenario: The test suite stays green

- **WHEN** the full test suite is run after the reorganization
- **THEN** every test MUST pass, and the only permitted change to the test inventory is the removal of tests covering deleted dead code.

#### Scenario: Test namespaces mirror the slices

- **WHEN** a test in `KotobaSenpai.Core.Tests` covering a `PhraseAnalysis` type is located
- **THEN** it MUST reside in a folder named after the same slice as the type under test.
