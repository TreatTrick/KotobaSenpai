# Stable Analysis Blocks Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep successfully analyzed, completed text blocks reusable when later OCR adds dialogue or narration, without clearing their annotations or resending their content.

**Architecture:** Preserve completed blocks from the latest completed analysis, together with matching active requests, when their normalized text and lexical token identities still match. Prepare one immutable layout for local grouping, cached preview, and provider analysis, including OCR line fragments when text reflows onto the same line. Keep natural sentence segmentation for content that has no preserved boundary.

**Tech Stack:** C# / .NET 10, xUnit, UniDic, existing OCR character geometry and phrase-analysis cache.

## Approved scope

- Work directly on local `main`, in this workspace, as explicitly requested.
- A successful block ending in a closing dialogue quote or sentence-final punctuation can retain its boundaries. Matching active requests may reserve the same boundaries to avoid duplicate overlapping analysis.
- Changed content invalidates only the affected block; incomplete typing remains eligible for regrouping.
- Cache identity retains the existing language, translation mode, lexical metadata, and boundary-punctuation rules.
- No automatic previous-text context is added to provider requests.

## Task 1: Reproduce loss of completed blocks

**Files:** `tests/KotobaSenpai.Core.Tests/Workflow/AutoRecognitionWorkflowTests.cs`, `tests/KotobaSenpai.Core.Tests/PhraseAnalysis/PhraseAnalysisCacheTests.cs`.

1. Add workflow regressions for dialogue followed by adjacent narration, a missing opening quote, same-line reflow, and replacement of one old block.
2. Withhold the new provider result and assert old group IDs, meanings, translations, and underlines remain available. Assert only new or changed source text reaches the provider.
3. Run `dotnet test tests/KotobaSenpai.Core.Tests/KotobaSenpai.Core.Tests.csproj --no-restore --filter FullyQualifiedName~Completed_block --verbosity minimal` and observe assertion failures before implementation.

## Task 2: Share a stable block plan

**Files:** `src/KotobaSenpai.Core/PhraseAnalysis/PhraseAnalysisPlan.cs`, `src/KotobaSenpai.Core/PhraseAnalysis/PhraseAnalysisOrchestrator.cs`, `src/KotobaSenpai.Core/Tokenization/SentenceSegmenter.cs`, `src/KotobaSenpai.Core/WordGrouping/IOcrWordGroupingService.cs`, `src/KotobaSenpai.Core/WordGrouping/WordGroupingService.cs`, `src/KotobaSenpai.Core/Workflow/WordOverlayApplicationService.cs`.

1. Introduce an immutable prepared plan carrying the OCR layout, segment boundaries, and provider requests for one recognition.
2. Match completed requests from the preceding plan against current OCR text at character/line boundaries; validate lexical identity before retaining a boundary. Use the existing successful cache and matching active requests to determine eligibility.
3. Preserve source character boxes while splitting OCR lines at retained boundaries. Naturally segment unmatched ranges.
4. Pass the same plan to grouping, cached preview, and analysis. Clear previous boundary state together with the cache, and preserve settings-specific cache isolation.
5. Run the new regressions and the existing cache, workflow, tokenization, and word-grouping tests.

## Task 3: Verify production tokenization and delivery

**Files:** `tests/KotobaSenpai.Platform.Windows.Tests/UniDicTokenizerTests.cs`, `openspec/specs/auto-recognition/spec.md`, this plan.

1. Replay completed dialogue followed by narration and same-line reflow using real UniDic and shared local grouping.
2. Confirm the provider sees only new content and old geometry, token references, and translations remain correct.
3. Build the app with `dotnet build src/KotobaSenpai.App/KotobaSenpai.App.csproj --no-restore --verbosity minimal`.
4. Run the solution suite, recording the pre-existing excluded theme/shortcut failure separately. Review the diff, then commit the verified change on `main`.

## Delivery evidence

- All three tasks completed in the authorized local `main` workspace.
- The initial workflow regression run failed five cases with missing cached groups; the cross-line quotation control passed.
- Completed dialogue followed by narration, missing opening quotes, same-line appends, selective content replacement, and translation-mode changes now preserve eligible boundaries and cached results.
- Incomplete or failed analysis remains regroupable. Cache reset removes completed boundaries and prevents previously prepared plans from starting requests or populating the new cache generation.
- Real UniDic and the production token-boundary span resolver passed both separate-line and same-line replays, including a removed opening quote and an added comma outside the closing quote.
- Application build: zero warnings and errors.
- Solution verification: 1,100 passed (Core 265, Windows 381, Architecture 38, App 416). The pre-existing `Open_popup_follows_application_theme_changes_without_replacing_content` theme/shortcut failure was excluded, as in baseline verification.
- Results: `artifacts/stable-analysis-blocks-results/`.
- Provider requests were replaced at the analyzer interface in tests; verification used no provider tokens.

An appended reporting clause is analyzed as new content once its preceding quotation has been completed and cached. Cross-block grammar groups and automatic previous-text context are deliberately deferred. An initial quotation and reporting clause observed together still use natural sentence segmentation. Live gameplay requires verification after restarting the rebuilt application.
