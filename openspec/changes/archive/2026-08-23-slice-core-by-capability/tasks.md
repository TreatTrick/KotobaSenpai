# Tasks: Slice Core by capability

Behavior must not change. Build and run the full test suite after every task; any drift from the
baseline is a defect, not an expected consequence of moving files.

## 1. Baseline and semantic corrections

- [x] 1.1 Record the baseline: run `dotnet build KotobaSenpai.slnx` and `dotnet test KotobaSenpai.slnx`, and confirm zero warnings, zero errors, and 277 passing tests.
- [x] 1.2 Delete the `CoordinateMapper.ToScreen(OcrWord, int, int, ScreenRect)` overload together with the two tests that exercise only it (the dead-code cases in `WindowWordOverlayTests` and `CoordinateTransformTests`). Confirm the build stays clean and 275 tests pass.
- [x] 1.3 Fold `WordMeaningView.FromWord(GroupedWord)` into `WordOverlaySession` as a private helper and remove every `GroupedWord` reference from `WordMeaningView`, including the `<see cref>` in its doc comment, so phrase analysis no longer references word grouping.
- [x] 1.4 Compute the type-to-slice dependency graph for the assignment table in `design.md` and confirm it is acyclic and consistent with the layer ordering, before moving any file.

## 2. Create slice folders and relocate layer 0

- [x] 2.1 Create the slice folders under `src/KotobaSenpai.Core/`: `Geometry`, `Errors`, `TargetWindow`, `Ocr`, `Tokenization`, `Dictionary`, `WordGrouping`, `PhraseAnalysis`, `Diagnostics`, `Overlay`, `Workflow`.
- [x] 2.2 Move `ScreenRect`, `NormalizedRect`, and `CoordinateMapper` into `Geometry`, update their namespaces to `KotobaSenpai.Core.Geometry`, and repair all references across `src` and `tests`.
- [x] 2.3 Move `BusinessRuleValidationException` and `InvalidFrameException` into `Errors`, and move `ErrorCodes` and `IUserFacingException` out of `Localization` into `Errors`, updating namespaces to `KotobaSenpai.Core.Errors` and repairing all references.
- [x] 2.4 Repair the `<see cref="IUserFacingException"/>` reference in `IUserMessageResolver` and sweep the whole solution for any other cross-namespace `cref` broken by the moves, so the build reports zero `CS1574` warnings.
- [x] 2.5 Confirm `Japanese`, `Localization`, `Logging`, and `Settings` need no file moves and that each has zero outgoing dependencies on other Core slices.

## 3. Relocate the pipeline slices

Move one slice at a time, updating its namespace and repairing references before starting the next,
so the solution compiles after every step.

- [x] 3.1 Move `WindowTarget`, `TargetWindowSnapshot`, `CapturedFrame`, `RecognitionRegion`, `CropRegion`, `IWindowCatalog`, `IWindowFrameCapture`, `ITargetWindowTracker`, and `IRegionSelector` into `TargetWindow`.
- [x] 3.2 Move `OcrLine`, `OcrWord`, and `IWindowWordRecognizer` (with `WordRecognitionResult`) into `Ocr`.
- [x] 3.3 Move `Token` (with `PartsOfSpeech`), `SentenceSegment`, `SentenceTokenId`, `SentenceTokenReference`, `PitchAccentSummary`, `SentenceSegmenter`, `LineBlockTokenizer`, and `ITokenizer` into `Tokenization`.
- [x] 3.4 Move `DictionaryEntry`, `DictionarySense`, `LookupSpan`, `JmdictLookupService`, `TokenBoundarySpanResolver`, `IDictionaryLookup`, `IBatchDictionaryLookup`, `IJmdictRepository`, and `ITokenSpanResolver` into `Dictionary`.
- [x] 3.5 Move `GroupedWord`, `WordGroupingService`, and `IOcrWordGroupingService` into `WordGrouping`.
- [x] 3.6 Move the phrase-analysis types into `PhraseAnalysis`: `PhraseAnalysisRequest`, `PhraseAnalysisResult`, `PhraseAnalysisRun` (with `PhraseGroupView`, `PhrasePartView`, `PhraseAnalysisOutcome`), `PhraseGroup`, `PhraseGroupPart`, `ParsedPhraseGroup`, `ParsedWordMeaning`, `WordMeaningView`, `LocalSpanSummary`, `PhraseAnalysisOrchestrator`, `PhraseGroupValidator`, `WordMeaningValidator`, `ILlmPhraseAnalyzer`, and — relocated out of their current slices — `SentenceTokenBuilder` and `PhraseGeometryMapper`.
- [x] 3.7 Move `IDiagnosticReporter` into `Diagnostics`.
- [x] 3.8 Move `WordOverlaySession`, `OverlayLine`, `NormalizedOverlayGeometry` (with its three normalized geometry records), `PhraseHoverResolver`, and `IOverlayRenderer` into `Overlay`.
- [x] 3.9 Move `WordOverlayApplicationService` into `Workflow`.
- [x] 3.10 Delete the now-empty `Models`, `Services`, and `Contracts` folders and confirm no type remains in a namespace ending in `.Models`, `.Services`, or `.Contracts`.
- [x] 3.11 Confirm `App.xaml.cs` service registrations and `MainWindowViewModel` compile against the new namespaces with no logic change, and that `CompositionRootTests` still passes.

## 4. Mirror the slices in the Core test project

- [x] 4.1 Create matching slice folders in `tests/KotobaSenpai.Core.Tests` and move each test file into the folder matching the slice of the type it covers.
- [x] 4.2 Update the test namespaces and `using` directives, and confirm all tests still pass.

## 5. Enforce the structure

- [x] 5.1 Add a layer-order `[Theory]` to `KotobaSenpai.Architecture.Tests` driven by one `(slice, layer)` table, asserting that no slice depends on another at the same or a higher layer, using the layer assignment from `design.md`.
- [x] 5.2 Add an architecture test asserting that no Core namespace segment is named `Shared`, `Common`, `Util`, `Utils`, `Helpers`, `Misc`, or `Infrastructure`.
- [x] 5.3 Add a repository-root `.editorconfig` setting `dotnet_diagnostic.IDE0130.severity = error`, and confirm the solution still builds with zero warnings and zero errors.
- [x] 5.4 Confirm the twelve pre-existing rules in `DependencyDirectionTests` still pass unchanged.
- [x] 5.5 Negative-test both guards: temporarily introduce a same-layer dependency and a file whose namespace disagrees with its folder, confirm each fails the build or the test suite with a message naming the offender, then revert.

## 6. Verification and housekeeping

- [x] 6.1 Run the full suite and confirm acceptance: zero warnings, zero errors, and 275 passing tests (the 277 baseline minus the two dead-code tests removed in task 1.2).
- [x] 6.2 Record the hash of the mechanical relocation commit in a repository-root `.git-blame-ignore-revs`.
- [x] 6.3 Delete the empty `openspec/changes/follow-target-window/` directory left behind after archiving.
- [x] 6.4 Confirm no user-visible text, configuration key, log message, or error code changed, and that `Platform.Windows` and `App` contain `using`-only edits with no logic change.
