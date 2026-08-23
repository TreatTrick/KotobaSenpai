## Context

`KotobaSenpai.Core` holds 69 source files. 59 of them live in three flat namespaces: `Models` (31), `Services` (14), and `Contracts` (14). The remaining 10 are the cross-cutting ports in `Localization` (4), `Japanese` (3), `Logging` (2), and `Settings` (1).

The cross-project dependency direction is already sound. `DependencyDirectionTests` pins twelve NetArchTest rules covering the hexagonal boundaries: Core depends on neither `Platform.Windows` nor `App`, adapters point inward, ViewModels touch only Core ports, and each cross-cutting port sits in Core with its implementation in App. That structure is not in question here.

What is in question is organization inside Core. Because files are grouped by technical type, a single capability is split across three folders, and files sort alphabetically within each, so nothing indicates which types belong together. Mapping the existing files onto business capabilities surfaces what the flat layout hides: seven types sit in a slice other than the one their dependencies imply, producing four reverse-dependency pairs, and one public method has no production caller at all.

The domain itself is a pipeline — capture, OCR, tokenize, group, look up, analyze, render — with exactly one aggregate root (`WordOverlaySession`); everything else is a value object or a stateless transformation. There is no rich object graph to partition by aggregate.

Baseline measured before any change: `dotnet build` reports zero warnings and zero errors; 277 tests pass in roughly four seconds.

## Goals / Non-Goals

**Goals:**

- Make a capability's value objects, domain services, and ports discoverable in one folder, so changing a capability requires opening one directory rather than three.
- Correct the seven misplaced responsibilities so that folder structure and real dependencies agree.
- Turn the resulting dependency layering into an automated constraint, so the structure cannot silently rot the way the current one did.
- Enforce namespace-to-folder agreement at compile time rather than by review.
- Preserve behavior exactly: no change to runtime output, user-visible text, configuration keys, or the surface consumed by adapters and the composition root.

**Non-Goals:**

- Changing the cross-project dependency direction or the twelve existing architecture rules.
- Reorganizing `Platform.Windows`. It is already vertically sliced, and its technology-oriented names (`Capture`, `Llm`, `Ocr`) are correct for an adapter layer: they survive swapping GDI capture for DXGI, or a hosted model for a local one, whereas capability names would not.
- Reorganizing `App`. It is a composition root plus a view layer; grouping by technical concern (`ViewModels`, `Themes`, `Logging`) is the right axis there.
- Introducing domain events, repository abstractions, CQRS, or per-slice assemblies. With one aggregate root in a single-process desktop application these are pure liability.
- Forcing code slices to correspond one-to-one with the nineteen entries under `openspec/specs/`. Those are a change taxonomy; slices are a structural taxonomy. The axes differ and aligning them would distort both.

## Decisions

### 1. Slice by capability, not by aggregate root

The original framing proposed classifying by aggregate root. Applied literally that yields one large bucket plus thirty-one orphans, because `WordOverlaySession` is the only type with identity, invariants, and a lifecycle. Every other type is a `record` value object or a stateless service.

The honest partition axis for a pipeline domain is the capability each stage serves. Eleven slices emerge, and every one of the 69 files lands in exactly one of them with no residue — which is itself evidence that the boundaries were already latent in the code and merely obscured by the folder layout.

Alternatives considered: sub-grouping inside the existing `Models`/`Services`/`Contracts` folders keeps the diff smaller but still requires three folder visits per capability, solving only part of the problem. Reorganizing only `Models` is smaller still, but leaves ports and services stranded. Writing an index document instead of moving code leaves the IDE file tree unimproved and rots on first divergence.

### 2. Ports live with their consumers; `Contracts` is dissolved

A central `Contracts` folder is the same disease as a central `Models` folder: it groups by what a type *is* rather than what it *serves*. Ports move into the slice that consumes them — `ITokenizer` into `Tokenization`, `IJmdictRepository` into `Dictionary`, `IOverlayRenderer` into `Overlay`. This keeps the hexagonal property intact (ports still live in Core, implementations still live in the adapter projects) while making the port visible next to the code that depends on it.

### 3. No `Shared` bucket; three named layer-0 slices instead

The types that multiple slices need are not "shared miscellany" — they are three cohesive groups with real names: coordinates (`ScreenRect`, `NormalizedRect`, `CoordinateMapper`), Japanese text primitives (`Kana`, `PitchAccent`, `UniDicAssets`), and error modeling (`BusinessRuleValidationException`, `InvalidFrameException`, `ErrorCodes`, `IUserFacingException`).

Naming a folder `Shared` or `Common` is an open invitation to deposit anything into it. Naming them `Geometry`, `Japanese`, and `Errors` forces a new file to answer which subject it belongs to, and a file that answers none does not belong at layer 0 at all.

`ErrorCodes` and `IUserFacingException` currently sit in `Localization`. They were filed there by association — error messages get translated — but neither carries any localization responsibility: one is a table of string constants, the other a marker interface. They move to `Errors`, leaving `Localization` holding only `IStringLocalizer` and `IUserMessageResolver`.

### 4. The cross-cutting ports join the layer table at layer 0

`Localization`, `Logging`, and `Settings` were initially treated as outside the layering. Inspection shows all three are BCL-only interfaces with zero outgoing dependencies — `ILogger` documents explicitly that it avoids referencing `Core.Localization` so the two cross-cutting ports stay decoupled. They therefore qualify as layer 0 alongside `Geometry`, `Japanese`, and `Errors`, and including them extends the enforced constraint to cover them at no cost.

### 5. Correct seven misplacements before moving any folder

| Type or member | Currently implies | Correct slice | Reverse dependency removed |
| --- | --- | --- | --- |
| `SentenceTokenBuilder` | `Tokenization` (depends on `ITokenSpanResolver`) | `PhraseAnalysis` — its only consumer is `PhraseAnalysisOrchestrator` | `Tokenization` → `Dictionary` |
| `LocalSpanSummary` | ambiguous; produced by tokenization, consumed by phrase analysis | `PhraseAnalysis` — it is a request DTO | `Tokenization` → `WordGrouping` |
| `PhraseGeometryMapper` | `Overlay` by name | `PhraseAnalysis` — both its input and output are phrase-analysis types | `PhraseAnalysis` → `Overlay` |
| `IDiagnosticReporter` | shared kernel | its own `Diagnostics` slice — it knowingly references `WindowTarget`, `GroupedWord`, and `PhraseGroupView`, which disqualifies it from layer 0 | `Shared` → three downstream slices |
| `ErrorCodes`, `IUserFacingException` | `Localization` | `Errors` | none; corrects filing by association |
| `WordMeaningView.FromWord(GroupedWord)` | `PhraseAnalysis` | folded into `WordOverlaySession`, its only caller | `PhraseAnalysis` → `WordGrouping` |
| `WordOverlayApplicationService` | one of fourteen files in `Services` | its own `Workflow` slice | see decision 7 |

### 6. Delete the dead `CoordinateMapper` overload rather than relocate it

`CoordinateMapper.ToScreen(OcrWord, int, int, ScreenRect)` has no production caller; it is kept alive solely by `WindowWordOverlayTests` and `CoordinateTransformTests`. Relocating it would drag an `Ocr` dependency into what is otherwise a zero-outgoing-dependency `Geometry` slice, purely to serve two tests of unused code. It is deleted along with those two tests. The remaining `ToScreen(ScreenRect, …)` overload carries all real callers and keeps `Geometry` at layer 0.

### 7. `Workflow` is separate from `Overlay`

`WordOverlayApplicationService` orchestrates the entire chain — target validation, capture, OCR, grouping, coordinate mapping, phrase analysis, overlay publication — and constructs the `WordOverlaySession` that `Overlay` owns. It is strictly above `Overlay`, and it is the single entry point into the domain. `MainWindowViewModel` already names its field `_workflow`, so the caller has independently reached the same conclusion.

Filing it under `Overlay` would mean that anyone asking where the recognition flow begins must first guess to look in a folder named after the output artifact. A one-file folder is justified here because the layering rule then yields a further guarantee for free: no slice may depend on `Workflow`, so the domain can never call back into its own use case.

Alternatives considered: `Application` is the conventional Clean Architecture name but injects a technical term into an otherwise capability-named structure. `Recognition` matches the project's ubiquitous language, but `recognitionId`, `RecognitionRegion`, and `IWindowWordRecognizer` already use that word inside other slices, so it would collide.

### 8. Enforce with a layer-order theory plus IDE0130

Two different failure modes need two different guards. NetArchTest checks compiled assembly references, so it catches a wrong *dependency*; it cannot see that a file sits in the wrong folder as long as the namespace is consistent. `IDE0130` catches a wrong *placement* at compile time but says nothing about direction. Together they close both.

The dependency rule is a single `[Theory]` driven by one `(slice, layer)` table asserting that a slice depends on nothing at its own layer or above. This forbids cycles by construction, forbids upstream dependencies, and — because siblings share a layer number — forbids independent capabilities from reaching into each other. Adding a legal downward dependency later requires no test edit.

Alternatives considered: an explicit per-slice allow-list is stricter still, catching unintended downward edges too, but every legitimate new dependency then requires editing the table, which creates steady pressure to edit the test until it passes. Guarding only layer-0 purity is cheapest but would have caught none of the four reverse-dependency pairs found here, since all of them occur between middle layers.

### 9. Independent siblings share a layer number

Assigning every slice a distinct layer number would only forbid cycles and upstream references. Where two slices are genuinely independent — `WordGrouping` and `PhraseAnalysis` after the fixes in decision 5, `Diagnostics` and `Overlay`, and the six layer-0 slices — giving them the same number additionally forbids either from referencing the other. The cost is that creating such an edge later requires editing one table row, which is the correct amount of friction for a decision that should be deliberate.

### 10. Separate semantic work from mechanical work in the commit history

The change contains two kinds of work with opposite review characteristics. The semantic edits are small, carry judgment, and can alter behavior if wrong. The relocation is enormous, carries no judgment, and fails the compiler immediately if wrong. Committing them together buries the two edits that need scrutiny inside a hundred-file diff and makes bisection useless. They are therefore separated into distinct phases.

Note that most entries in decision 5's table are *relocations* rather than code edits: with everything currently flat in `Services`, moving `SentenceTokenBuilder` from `Tokenization` to `PhraseAnalysis` is a no-op until the folders exist. Only two items are genuinely folder-independent code changes — the dead-code deletion and folding `WordMeaningView.FromWord` into `WordOverlaySession`. Phase 1 therefore consists of those two edits plus a verification that the intended assignment yields an acyclic graph; the rest lands with the relocation in phase 2.

## Target slice assignment

| Layer | Slice | Files |
| --- | --- | --- |
| 0 | `Geometry` | `ScreenRect`, `NormalizedRect`, `CoordinateMapper` |
| 0 | `Japanese` | `Kana`, `PitchAccent`, `UniDicAssets` |
| 0 | `Errors` | `BusinessRuleValidationException`, `InvalidFrameException`, `ErrorCodes`, `IUserFacingException` |
| 0 | `Localization` | `IStringLocalizer`, `IUserMessageResolver` |
| 0 | `Logging` | `ILogger`, `LogLevel` |
| 0 | `Settings` | `ISettingsService` |
| 1 | `TargetWindow` | `WindowTarget`, `TargetWindowSnapshot`, `CapturedFrame`, `RecognitionRegion`, `CropRegion`, `IWindowCatalog`, `IWindowFrameCapture`, `ITargetWindowTracker`, `IRegionSelector` |
| 2 | `Ocr` | `OcrLine`, `OcrWord`, `IWindowWordRecognizer` (with `WordRecognitionResult`) |
| 3 | `Tokenization` | `Token` (with `PartsOfSpeech`), `SentenceSegment`, `SentenceTokenId`, `SentenceTokenReference`, `PitchAccentSummary`, `SentenceSegmenter`, `LineBlockTokenizer`, `ITokenizer` |
| 4 | `Dictionary` | `DictionaryEntry`, `DictionarySense`, `LookupSpan`, `JmdictLookupService`, `TokenBoundarySpanResolver`, `IDictionaryLookup`, `IBatchDictionaryLookup`, `IJmdictRepository`, `ITokenSpanResolver` |
| 5 | `WordGrouping` | `GroupedWord`, `WordGroupingService`, `IOcrWordGroupingService` |
| 5 | `PhraseAnalysis` | `PhraseAnalysisRequest`, `PhraseAnalysisResult`, `PhraseAnalysisRun` (with `PhraseGroupView`, `PhrasePartView`, `PhraseAnalysisOutcome`), `PhraseGroup`, `PhraseGroupPart`, `ParsedPhraseGroup`, `ParsedWordMeaning`, `WordMeaningView`, `LocalSpanSummary`, `PhraseAnalysisOrchestrator`, `PhraseGroupValidator`, `WordMeaningValidator`, `SentenceTokenBuilder`, `PhraseGeometryMapper`, `ILlmPhraseAnalyzer` |
| 6 | `Diagnostics` | `IDiagnosticReporter` |
| 6 | `Overlay` | `WordOverlaySession`, `OverlayLine`, `NormalizedOverlayGeometry` (with the three normalized geometry records), `PhraseHoverResolver`, `IOverlayRenderer` |
| 7 | `Workflow` | `WordOverlayApplicationService` |

All 69 Core files are accounted for.

## Risks / Trade-offs

- **A hundred-file diff obscures review and pollutes `git blame`.** → The mechanical relocation is isolated into one commit that contains no semantic content, and its hash is recorded in `.git-blame-ignore-revs` so blame skips it.
- **Moving `IUserFacingException` to `Errors` breaks the `<see cref="IUserFacingException"/>` in `IUserMessageResolver`'s XML documentation, producing `CS1574`.** → The baseline is zero warnings, so this must be repaired in the same commit by qualifying the cref or adding the using. Any other cross-namespace cref must be swept for the same reason.
- **NetArchTest can under-report dependencies expressed only through generics or compiler-generated members, and Core is dense with `record` types.** → The layer rule is a safety net, not the primary mechanism; the intended assignment is independently verified against a computed dependency graph during phase 1, before any file moves.
- **`Diagnostics` holds a single file.** → Accepted. Its layer position is what matters: it references three downstream slices, which disqualifies it from layer 0, and it is consumed only by `Workflow`.
- **Same-layer mutual prohibition creates friction when a genuinely needed sibling edge appears later.** → Intended. The remedy is a one-row table edit, made deliberately rather than by drift.
- **Sibling slices could be given the same layer number today and diverge in future refactors.** → The layer table is the single source of truth and lives beside the test that reads it, so both change together.

## Migration Plan

**Phase 1 — semantics (small commits, individually testable).**
Delete `CoordinateMapper.ToScreen(OcrWord, …)` together with the two tests that cover only it. Fold `WordMeaningView.FromWord(GroupedWord)` into `WordOverlaySession`. Verify the target assignment produces an acyclic graph with the intended layer ordering before any file moves. Build and test after each commit.

**Phase 2 — relocation (one commit, no semantic content).**
Create the eleven slice folders, move all 69 Core files to their assigned slice with `git mv`, update namespaces, and repair `using` directives across `Platform.Windows`, `App`, and the four test projects. Mirror the slice folders in `KotobaSenpai.Core.Tests`. Remove the now-empty `Models`, `Services`, and `Contracts` folders. Record the commit hash in `.git-blame-ignore-revs`.

**Phase 3 — enforcement.**
Add the layer-order `[Theory]` to `Architecture.Tests`. Add a repository-root `.editorconfig` setting `dotnet_diagnostic.IDE0130.severity = error`. Confirm the twelve pre-existing dependency-direction rules still pass unchanged.

**Housekeeping.** Delete the empty `openspec/changes/follow-target-window/` directory left behind after archiving.

**Rollback.** Each phase is a separate commit or commit group and is independently revertible. Phase 2 is a pure rename and reverts cleanly; phases 1 and 3 touch few files.

**Acceptance.** Zero warnings and zero errors from `dotnet build`; 275 tests green, being the 277-test baseline minus the two dead-code tests removed in phase 1.

## Open Questions

None. All boundary, naming, enforcement, and sequencing decisions were settled before this document was written.
