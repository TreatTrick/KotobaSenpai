## ADDED Requirements

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

Each Core capability slice SHALL be assigned a layer number. A slice MUST NOT depend on any other slice whose layer number is greater than or equal to its own. Slices sharing a layer number are therefore mutually forbidden from referencing each other, which enforces both acyclicity and the independence of sibling capabilities.

The layer assignment SHALL be:

| Layer | Slices |
| --- | --- |
| 0 | `Geometry`, `Japanese`, `Errors`, `Localization`, `Logging`, `Settings` |
| 1 | `TargetWindow` |
| 2 | `Ocr` |
| 3 | `Tokenization` |
| 4 | `Dictionary` |
| 5 | `WordGrouping`, `PhraseAnalysis` |
| 6 | `Diagnostics`, `Overlay` |
| 7 | `Workflow` |

#### Scenario: A slice referencing a higher layer fails the build

- **WHEN** a type in `KotobaSenpai.Core.Tokenization` acquires a dependency on `KotobaSenpai.Core.Dictionary`
- **THEN** the layer-order architecture test MUST fail and name the offending type.

#### Scenario: Sibling slices on the same layer stay independent

- **WHEN** a type in `KotobaSenpai.Core.PhraseAnalysis` acquires a dependency on `KotobaSenpai.Core.WordGrouping`, or vice versa
- **THEN** the layer-order architecture test MUST fail, because both slices occupy layer 5.

#### Scenario: Layer-0 slices have no outgoing slice dependencies

- **WHEN** the types in `Geometry`, `Japanese`, `Errors`, `Localization`, `Logging`, or `Settings` are checked
- **THEN** none of them SHALL depend on any other Core capability slice.

#### Scenario: The workflow slice is the sole domain entry point

- **WHEN** any Core slice other than `Workflow` is checked
- **THEN** it MUST NOT depend on `KotobaSenpai.Core.Workflow`, so the application-service entry point is never called back into from the domain.

#### Scenario: Adding a legal downward dependency needs no test change

- **WHEN** a type in `KotobaSenpai.Core.Overlay` (layer 6) starts depending on `KotobaSenpai.Core.Ocr` (layer 2)
- **THEN** the layer-order architecture test MUST pass without modification.

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
