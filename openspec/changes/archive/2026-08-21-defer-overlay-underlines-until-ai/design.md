## Context

Local OCR grouping and UniDic/JMdict resolution already produce the word surface and reading synchronously. `WordOverlayApplicationService` currently waits for the optional, concurrent phrase-analysis batch and then publishes one `WordOverlaySession`; `WpfOverlayRenderer` draws furigana and every word underline from that session. The requested behavior needs an initial local-only publication without weakening the existing disabled-LLM fallback.

## Goals / Non-Goals

**Goals:**

- Publish furigana immediately after local grouping when phrase analysis is enabled.
- Publish one final overlay after the complete LLM batch, with underlines and meanings only for successful sentence segments.
- Preserve the existing full local overlay when phrase analysis is disabled.
- Prevent a superseded recognition's late provider response from replacing a newer session.

**Non-Goals:**

- Changing furigana typography, placement, or the provider wire protocol.
- Streaming individual sentence responses into the overlay.
- Cancelling HTTP requests through a new application-wide cancellation service; generation checks provide the required visibility guarantee.

## Decisions

- **Represent staged rendering in the existing session.** Add an optional set of underline-eligible segment IDs to `WordOverlaySession`. A null set means legacy behavior (all words); an empty set renders furigana only; a non-empty set renders lines only for words belonging to successful segments. The renderer keeps one code path and continues to clear all elements on refresh.
- **Carry segment identity with local words.** `GroupedWord` gains an optional sentence segment ID, populated by `WordGroupingService` and preserved by coordinate remapping. Legacy constructors leave it null; those words remain underline-eligible in the default session mode.
- **Return successful segment IDs from analysis.** `PhraseAnalysisRun` exposes the request segment IDs whose provider result was successful, including successful responses with zero groups or zero validated meanings. The orchestrator still aggregates warnings and results in input order.
- **Publish in two phases in the application service.** After local grouping, if phrase analysis is enabled, show a session with no underline-eligible IDs. Await the full orchestrator batch, then show one replacement session using its successful segment IDs, groups, and meanings. If phrase analysis is disabled, show the legacy session immediately with all underlines eligible.
- **Guard publication by recognition generation.** Increment a private generation counter at the start of recognition and on hide. Check the captured generation before both `Show` calls; a stale run may finish its local/LLM work but cannot write to the overlay.
- **Keep hover behavior unchanged.** The initial session has no line elements, so hover polling has no underline targets; the final session reuses current word/group hover logic and popup fallback behavior.

## Risks / Trade-offs

- [Adding segment identity touches core model constructors] -> Keep the property optional and preserve existing constructor overloads/equality semantics; add focused compatibility tests.
- [A late old run can still consume provider resources] -> Generation checks prevent visible corruption; request cancellation remains an optimization for a later change.
- [Successful provider responses with no groups still enable local underlines] -> Track segment success separately from group/meaning counts so the contract matches the staged lifecycle.
- [Initial local-only overlay has no underline hit regions] -> This is intentional while AI is pending; furigana remains visible and the final refresh restores hover targets.

## Migration Plan

No data migration. Deploy the code and keep existing session call sites on the default all-underlines behavior. Rollback is a code revert; persisted settings and provider payloads are unchanged.
