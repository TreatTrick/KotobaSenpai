## 1. Core session and segment state

- [x] 1.1 Add optional sentence-segment identity to `GroupedWord` and preserve it through `WithRects`, while keeping legacy constructors and equality behavior compatible.
- [x] 1.2 Populate each grouped word's segment identity in `WordGroupingService`.
- [x] 1.3 Add underline eligibility state to `WordOverlaySession.Start`; keep the default session behavior as all local words underlined and support furigana-only sessions.
- [x] 1.4 Extend `PhraseAnalysisRun` and `PhraseAnalysisOrchestrator` to return successful sentence segment IDs, including successful responses with no groups.

## 2. Staged application workflow

- [x] 2.1 Publish the initial furigana-only session immediately after local grouping when phrase analysis is enabled.
- [x] 2.2 Publish exactly one final session after the complete analysis batch, passing successful segment IDs, phrase groups, warnings, and validated meanings.
- [x] 2.3 Preserve the existing immediate furigana-plus-underline session when phrase analysis is disabled.
- [x] 2.4 Add recognition-generation checks at recognition start, before each publish, and on hide so superseded runs cannot update the overlay.

## 3. Windows renderer

- [x] 3.1 Make `WpfOverlayRenderer` draw underline elements only for words eligible in the current session while always rendering eligible furigana.
- [x] 3.2 Keep hover bookkeeping, popup fallback, refresh clearing, DPI mapping, and click-through behavior correct for empty and populated underline sets.

## 4. Tests and verification

- [x] 4.1 Add core model/orchestrator tests for segment identity, successful-segment tracking, and partial provider failure.
- [x] 4.2 Add application-service tests proving early furigana publication, one final refresh, disabled-analysis compatibility, and failed-segment underline exclusion.
- [x] 4.3 Add stale-run and hide-race tests proving an older recognition cannot publish after being superseded.
- [x] 4.4 Run formatting/build and the focused Core, App, and Windows test suites; validate the OpenSpec change status.
