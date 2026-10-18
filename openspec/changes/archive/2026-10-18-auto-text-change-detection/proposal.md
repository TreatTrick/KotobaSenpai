## Why

Currently, users must manually trigger OCR recognition for each frame of text they want to translate. In GALGAME and other text-heavy applications, text often changes incrementally — a new line of dialogue appears while the previous line remains on screen. Requiring a full re-recognition and re-analysis for every minor change wastes both the user's attention and the LLM provider's compute (re-analyzing unchanged sentences costs tokens and latency each time).

This change introduces automatic, polling-based detection of text changes within the recognition region and an incremental recognition path that reuses cached LLM results for unchanged sentence segments. A settings toggle enables or disables the feature.

## What Changes

- Add a **text change detection** loop: periodically captures the recognition region, computes a perceptual or frame-difference hash, and fires a recognition cycle only when the content has materially changed.
- Add a **session-level LLM result cache** keyed by normalized sentence text: when a new recognition produces sentence segments whose text matches a previously cached result, the cached phrase groups and word meanings are reused without another LLM round-trip.
- Introduce an **incremental recognition mode** (opt-in via settings) that reuses the previous session's results for unchanged regions and runs OCR + LLM only for the new content.
- Add a **settings toggle** (`AutoRecognitionEnabled`) under settings to enable or disable automatic polling recognition.
- The manual "Recognize" button continues to work as before; auto mode is additive and never removes manual control.

## Capabilities

### New Capabilities
- `auto-recognition`: Periodic screen capture → change detection → conditional full or incremental recognition.
- `llm-result-cache`: Session-scoped cache mapping sentence text to prior LLM analysis results, enabling reuse across incremental recognition cycles.

### Modified Capabilities
- `settings`: New setting key `AutoRecognitionEnabled` (bool) to control whether automatic polling is active.
- `staged-overlay-analysis`: The staged overlay and post-analysis refresh now need to support partial replacement when only a subset of segments received new LLM results.
- `llm-phrase-groups`: The sentence-level analysis boundary must remain stable when only some segments are re-analyzed; unchanged segments keep their existing session group IDs.

## Impact

- New Core services: `IAutoRecognitionController` (poller + change detection), `ILlmResultCache` (sentence → result cache).
- Core `WordOverlayApplicationService` gains an incremental recognition path.
- Settings schema gains a boolean flag; UI gains a toggle.
- Platform layer gains a polling loop (`DispatcherTimer` or equivalent) with configurable interval and change-detection threshold.
- No breaking changes to existing manual recognition flow.
