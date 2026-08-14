## Why

The current Japanese analysis pipeline can resolve UniDic tokens and continuous JMdict spans, but it cannot reliably represent or explain combinations whose parts are separated by other words or OCR line breaks. This limits the product's core value for grammar patterns and fixed expressions in visual-novel dialogue. The MVP should use the LLM as the primary detector for meaningful combination groups while retaining UniDic/JMdict as grounded local evidence and fallback.

## What Changes

- Add an LLM-backed phrase-group analysis flow that receives sentence-level OCR text, stable token metadata, and locally resolved continuous spans.
- Make the LLM return only meaningful combination groups, including non-continuous grammar patterns, collocations, and context-dependent combinations; ordinary tokens and already-resolved continuous spans remain local results.
- Require every returned group part to reference existing token IDs; allow multiple contiguous parts per group so groups can span intervening tokens and OCR lines without highlighting the gap.
- Support sentence-level token IDs that preserve source line identity and screen geometry, including groups whose parts cross adjacent OCR lines.
- Add structured group data containing a request-local model ID, application-assigned session ID, type, label, Chinese meaning, and Chinese grammar explanation.
- Permit overlap between different groups. Use deterministic hover selection for overlapping groups and highlight all parts of the selected group together.
- Add bounded output (maximum eight groups per sentence segment), structural validation, and local-only fallback when the LLM is unavailable, times out, is refused, or returns invalid data.
- Keep the LLM contract provider-neutral while implementing the first provider through the existing DeepSeek-compatible BYOK direction. Send text and metadata only; never send screenshots in this change.

## Capabilities

### New Capabilities

- `llm-phrase-groups`: Discover and explain grounded multi-token combination groups using an LLM and stable token references.

### Modified Capabilities

- `word-grouping`: Extend token/geometry mapping from independent OCR lines to sentence-scoped token references and multi-part cross-line group geometry while preserving existing continuous local spans.
- `window-word-overlay`: Display and hover multi-part groups with a shared application group ID, including cross-line parts and overlapping groups.

## Impact

- Core models and contracts: sentence token references, phrase groups/parts, analysis result, provider port, validation errors, and grouping/session data.
- App/Platform.Windows orchestration: sentence segmentation, LLM request building, DeepSeek adapter, response parsing, fallback state, and DI registration.
- Overlay rendering: multiple geometries per group, shared highlighting, and deterministic overlap hover behavior.
- Tests and documentation: JSON contract/validation tests, cross-line mapping tests, provider mock tests, and golden phrase-group examples.
- Runtime dependency: optional network/API access through the user's configured provider; local UniDic/JMdict behavior remains usable without it.
