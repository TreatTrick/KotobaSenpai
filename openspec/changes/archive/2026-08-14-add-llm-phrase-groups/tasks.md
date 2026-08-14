## 1. Core Contracts and Models

- [x] 1.1 Add immutable sentence/token-reference models with request-scoped IDs (`lineId:tokenIndex`), source line identity, local offsets, and UniDic metadata.
- [x] 1.2 Add immutable phrase group models for model ID, application session ID, type, parts, label, Chinese meaning, grammar explanation, and provider order.
- [x] 1.3 Add phrase-analysis request/response contracts and a provider-neutral `ILlmPhraseAnalyzer` port with cancellation and diagnostic/error semantics.
- [x] 1.4 Add validation utilities for token ownership, contiguous ordered parts, duplicate references, segment ownership, required field lengths, and the eight-group limit.

## 2. Sentence Segmentation and Geometry

- [x] 2.1 Add deterministic OCR-line segmentation that preserves reading order and joins only adjacent lines with reliable order/layout and no sentence-final or paragraph boundary.
- [x] 2.2 Build sentence-scoped token references while preserving existing per-line UniDic/JMdict span resolution and local offsets.
- [x] 2.3 Add phrase-part geometry mapping from referenced tokens to per-line character-box rectangles; ensure gaps and unrelated tokens are excluded.
- [x] 2.4 Add unit tests for same-line groups, cross-line continuous parts, separated multi-part groups, invalid line joins, and empty geometry.

## 3. LLM Request and Provider Adapter

- [x] 3.1 Add a request builder that serializes only segment text, token metadata, and local continuous span summaries, with a prompt requiring combination groups only and token-ID references.
- [x] 3.2 Add strict JSON DTO parsing and conversion to Core response models; reject screenshots, offsets, unknown fields where configured, oversized text, and invalid group shapes.
- [x] 3.3 Implement the first `ILlmPhraseAnalyzer` adapter for the configured DeepSeek-compatible endpoint using BYOK settings, cancellation, timeout, and redacted diagnostics.
- [x] 3.4 Add provider mock tests for valid multi-part output, cross-line references, empty groups, malformed JSON, refusal, timeout, cancellation, and API-key absence.

## 4. Analysis Orchestration and Fallback

- [x] 4.1 Add orchestration that completes local tokenization/span resolution first, segments OCR lines, invokes the provider once per sentence segment, validates groups, assigns session group IDs, and preserves provider order.
- [x] 4.2 Ensure provider failures or all-invalid responses return local words/spans plus a retryable phrase-analysis warning without throwing through the recognition flow.
- [x] 4.3 Register the provider-neutral port, DeepSeek adapter, request builder, validator, and orchestrator in DI without changing existing local lookup registrations.
- [x] 4.4 Add integration tests proving local-only output is unchanged when phrase analysis is disabled or unavailable.

## 5. Overlay and Interaction

- [x] 5.1 Extend overlay session data to carry phrase groups and per-part geometries while retaining existing local word lines and popup behavior.
- [x] 5.2 Render phrase-part markers and maintain a shared application group ID across all parts, including parts on different OCR lines.
- [x] 5.3 Extend hit testing so any part selects its group, highlights all parts, and opens one detail panel with label, Chinese meaning, and grammar explanation.
- [x] 5.4 Implement overlap hover priority: fewest distinct referenced tokens first, then provider response order; preserve click-through and restore colors on leave.
- [x] 5.5 Add overlay tests for multi-part highlighting, cross-line geometry, overlap priority, refresh/hide cleanup, and local-word regression behavior.

## 6. Documentation and Evaluation

- [x] 6.1 Document the provider payload contract, token-ID rules, privacy boundary, fallback behavior, and DeepSeek configuration.
- [x] 6.2 Add a small golden set of representative VN sentences covering non-continuous grammar, inflection, cross-line words, overlaps, and no-group cases.
- [x] 6.3 Add diagnostics that record segment ID, token/group counts, provider outcome, and validation warnings without logging screenshots, API keys, or window titles.
- [x] 6.4 Run the existing Core/App/Platform test suites plus the new phrase-group tests and record the MVP evaluation gaps.
