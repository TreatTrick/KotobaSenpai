## Context

The current pipeline tokenizes each OCR line independently, resolves continuous token-boundary spans against JMdict, and renders one `GroupedWord`/underline per resolved span. `LookupSpan` intentionally models one continuous interval, so it is not suitable for a group whose meaningful parts are separated by other tokens or by an OCR line break. The repository has no LLM provider or phrase-group contract yet, while the product goal requires contextual explanations for combinations such as `〜ないことには〜ない`.

This change is an MVP integration design. The LLM is the primary detector and explainer for combination groups. UniDic and JMdict remain the authoritative local tokenization/continuous lookup evidence, geometry source, and offline fallback. The design must preserve the existing local word path when no API key, network, or valid model response is available.

## Goals / Non-Goals

**Goals:**

- Analyze one or more OCR lines as sentence segments with stable, request-scoped token IDs.
- Send token metadata and local continuous spans, but no screenshots, to a provider-neutral phrase analyzer.
- Accept only groups whose parts reference known tokens; derive surface text, readings, and screen geometry locally.
- Support one or more contiguous token parts per group, including a single continuous part crossing an accepted OCR line break and multiple parts separated by gaps.
- Generate application-owned session group IDs, preserve model order, cap output at eight groups per sentence segment, and support deterministic hover selection for overlapping groups.
- Keep local UniDic/JMdict words and spans visible when phrase analysis fails.

**Non-Goals:**

- No complete local grammar-rule catalog or local phrase ranking engine.
- No unrestricted character-offset or free-text span returned by the model.
- No screenshot/image upload, model fine-tuning, or provider-specific types in Core.
- No cross-paragraph or uncertain OCR-line composition.
- No global conflict optimization or automatic removal of overlapping groups.
- No requirement that the LLM rediscover ordinary tokens or continuous JMdict spans.

## Decisions

### 1. Add a provider-neutral phrase analysis port

Core will define immutable request/response models and an interface such as `ILlmPhraseAnalyzer`. The request contains sentence segments, token references (surface, lemma, reading, POS, conjugation metadata, line ID, and local continuous span summaries), plus the original segment text. The response contains model groups and provider diagnostics. A DeepSeek-compatible adapter lives outside Core and is the first implementation; BYOK settings remain owned by the existing application settings path.

**Alternatives considered:** Put DeepSeek request/JSON types in Core (rejected because it couples the domain to one vendor); call the provider directly from the overlay (rejected because it makes cancellation, fallback, and validation impossible to test centrally).

### 2. Use sentence-scoped token references

Before phrase analysis, the orchestrator preserves OCR reading order and assigns IDs such as `l0:t3` to tokens. Each reference keeps its source line index, token index, UTF-16 offset within that line, surface/reading, and source character boxes. Existing per-line offsets remain valid for local span resolution; the sentence model adds the line identity needed for cross-line references.

Adjacent OCR lines are joined only when local segmentation considers their order reliable and their layout gap plausible. A line ending in sentence-final punctuation (`。`, `！`, `？`, `…` and equivalents), a clear paragraph/layout gap, or uncertain reading order starts a new segment. The LLM receives one segment at a time and cannot join IDs from different segments.

**Alternatives considered:** Use one global character offset for the whole capture (rejected because it obscures line geometry and makes OCR edits harder to map); let the LLM decide line joins (rejected because boundary errors would be nondeterministic and can combine separate dialogue boxes).

### 3. Make group parts token references, not offsets

The model response schema contains a request-local `modelGroupId`, `type`, `parts`, `label`, `meaningZh`, `grammarZh`, and optional confidence/reason. Each part is a non-empty ordered list of known token IDs with no gaps inside the part. A group has one or more parts. Parts may be separated from each other by arbitrary intervening tokens within the same sentence segment. Different groups may overlap; one group may not repeat a token within its own parts.

The application validates IDs, ordering, segment ownership, duplicate references, field lengths, group count (maximum eight), and required text fields. Invalid groups are dropped without invalidating valid groups. The application derives each part's displayed surface, reading, and one or more screen rectangles from the referenced OCR characters; it never trusts model offsets or model-provided surface text for geometry.

**Alternatives considered:** Accept arbitrary character offsets (rejected because UTF-16/OCR offset errors can highlight unrelated text); change `LookupSpan` to sparse tokens (rejected because its single interval and existing popup/underline semantics are intentionally continuous).

### 4. Keep selection simple and deterministic

The model's returned order is preserved. The UI renders all valid groups, including overlaps, with a shared application-owned session `GroupId` across all parts. When the cursor hits multiple group parts, the hover resolver chooses the group with the fewest distinct referenced tokens; ties use model response order. The selected group highlights all of its parts and owns one detail popup.

**Alternatives considered:** Add a weighted interval scheduler or learned reranker (rejected for MVP because it adds complexity before a labeled evaluation set exists); hide all overlap (rejected because nested grammar and continuous evidence can be useful together).

### 5. Treat provider failure as an optional enrichment failure

The local recognition flow completes words and continuous spans before phrase analysis. Timeout, cancellation, missing key, transport failure, provider refusal, malformed JSON, or a response with no valid groups yields a warning/retry state and an otherwise usable local overlay. No partially parsed group is rendered as valid.

### 6. Keep request size and exposure bounded

Only the current OCR text, token metadata, and local continuous span summaries are sent. Screenshots, window titles, screen coordinates, API keys, and unrelated OCR segments are excluded. The request builder caps segment text/token metadata according to provider limits and records a redacted diagnostic. Caching is out of scope for this change but the request model must be stable enough for a later cache key.

## Risks / Trade-offs

- **[LLM false positives]** → The model is explicitly told to return only meaningful combination groups, local validation enforces token ownership, and the UI caps groups at eight; a later golden set can measure precision/recall.
- **[LLM misses useful expressions]** → Preserve all local words/spans and make phrase analysis retryable; add local rules only from observed misses after MVP evaluation.
- **[Cross-line mis-grouping]** → Use deterministic line-order/layout/punctuation segmentation and prohibit cross-segment references.
- **[Provider latency/cost]** → Analyze one sentence segment per request, omit ordinary word results, cap metadata/output, allow cancellation, and keep local results immediately available.
- **[Malformed or unsafe output]** → Strict JSON/schema/length validation, drop invalid groups individually, and never use model offsets or raw HTML/UI markup.
- **[Overlapping geometry]** → Store multiple part rectangles and a shared session group ID; choose one popup deterministically while retaining all groups for future interaction improvements.

## Migration Plan

1. Add Core phrase contracts and sentence/token-reference mapping without changing existing `LookupSpan` behavior.
2. Add a provider adapter and orchestration behind a feature flag or disabled-by-default setting; verify local-only output is unchanged.
3. Add phrase geometry/session fields and overlay hover/highlight behavior.
4. Enable the feature for configured BYOK users; on any failure, keep the local path and expose retry.
5. Rollback is configuration-level: disable phrase analysis and continue using existing local grouping/overlay. No persisted schema migration is required.

## Open Questions

- Exact DeepSeek model name, endpoint defaults, and structured-output mode must follow the current provider/API configuration available when implementation starts.
- The initial UI presentation for multiple group labels/grammar explanations needs a screen-level design; the contract only fixes data and hover behavior.
- A representative labeled VN sentence set is still needed to measure whether LLM-first discovery is useful enough to justify follow-up local rules.
