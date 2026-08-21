## Why

UniDic already resolves each recognized word's reading locally, but the current workflow waits for the optional LLM analysis before showing any overlay content. This makes the first useful visual feedback unnecessarily dependent on network latency and hides the furigana that is available immediately.

## What Changes

- Show locally resolved furigana as soon as UniDic-based grouping finishes.
- When phrase analysis is enabled, defer local word underlines and LLM meanings until all concurrent sentence requests complete.
- Apply one overlay refresh after the LLM run: successful sentence results add underlines, phrase groups, and validated word meanings; failed sentences retain furigana only and contribute a retryable warning.
- Preserve the existing furigana-plus-underline behavior when phrase analysis is disabled.
- Ensure a newer recognition run supersedes any older in-flight LLM result.

## Capabilities

### New Capabilities

- `staged-overlay-analysis`: Defines the two-stage local-furigana and post-LLM overlay lifecycle.

### Modified Capabilities

- `window-word-overlay`: Overlay refreshes may initially contain furigana without underlines while an enabled LLM analysis is pending.
- `llm-phrase-groups`: Partial provider failure and superseded runs must preserve the newest local overlay and apply only valid results after the complete batch.
- `llm-word-meanings`: Meanings are attached during the post-analysis overlay refresh rather than delaying initial local feedback.

## Impact

- `WordOverlayApplicationService` and `PhraseAnalysisOrchestrator` need staged publication and stale-run protection.
- `WordOverlaySession`/`IOverlayRenderer` need an explicit way to render furigana without underlines while analysis is pending.
- `WpfOverlayRenderer` must render the staged state without changing furigana geometry or hover behavior.
- Core and Windows tests need coverage for timing, partial success, disabled analysis, and superseded requests.
- No new external dependencies or persisted-data migration is required.
