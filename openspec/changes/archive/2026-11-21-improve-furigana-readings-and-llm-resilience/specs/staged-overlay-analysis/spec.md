## MODIFIED Requirements

### Requirement: Stage local furigana before cloud analysis
After stable local recognition and grouping, the application SHALL publish locally resolved words and furigana（使用“JMdict 多词唯一命中回流读音 > UniDic 原读音”的本地有效读音）without waiting for provider I/O. Cached successful segments SHALL retain their underlines, groups, meanings, validated LLM reading overrides, and corresponding translations. Segments without successful analysis SHALL have local annotations only. Historical enable settings SHALL NOT bypass cloud analysis. Publication SHALL update the saved session while target visibility rules determine whether it is displayed; temporary hiding due to target invisibility, minimization, or an occlusion union strictly greater than 30% of the full client area SHALL retain the latest completed result for restoration.

#### Scenario: Initial overlay appears before a slow provider with JMdict reading
- **WHEN** initial or full-change local recognition and grouping finish（例如包含 `頭を垂れる` 或 `一週間`）, the provider has not returned yet, and the target is renderable
- **THEN** the overlay is visible immediately with locally resolved furigana（`こうべ` / `た`、`いっしゅうかん`）for eligible words and without word underlines or LLM meanings

#### Scenario: Historical disabled-analysis setting does not bypass analysis
- **WHEN** a historical setting disables phrase analysis
- **THEN** the application still enters cloud analysis and publishes only local furigana while waiting, without successful-analysis underlines

#### Scenario: Incremental recognition preserves prior overlay and cached reading overrides during analysis
- **WHEN** incremental recognition is in progress (some segments reuse cached LLM results containing validated reading overrides, others need fresh analysis)
- **THEN** the prior overlay session SHALL NOT be cleared; unchanged word underlines, phrase groups, word meanings, and cached reading overrides SHALL remain visible while the target is renderable until the incremental cycle completes

#### Scenario: Incremental cycle completion adds the fresh annotations
- **WHEN** all incremental segments (both cached and fresh) are processed
- **THEN** the application SHALL issue one post-analysis publication containing the merged result and reuse unchanged annotation elements from the local preview

#### Scenario: A second sentence preserves existing drawing elements
- **WHEN** a stable new sentence is appended while previous words keep the same text, effective reading, override state, geometry, underline eligibility, and font scale
- **THEN** existing furigana, pitch markers, and underlines remain the same drawing elements; the renderer adds or replaces only affected word annotations

### Requirement: Publish one post-analysis overlay refresh
After all sentence requests in an analysis batch complete, the application SHALL replace the staged overlay exactly once. The replacement SHALL add underlines, validated meanings, and validated LLM contextual reading overrides for successful sentence segments, preserve local furigana for words without reading overrides or from failed segments, and retain a warning for failed segments. 在增量模式下，替换 SHALL 保留上一轮缓存命中段的下划线、短语组和已校验读音纠偏，仅新增或更新有变化的段。

#### Scenario: All requests succeed with contextual reading override
- **WHEN** every sentence request completes successfully and at least one word carries a validated LLM reading override（例如 `額` 由 `がく` 纠偏为 `ひたい`）
- **THEN** one replacement overlay atomically updates that word's furigana and popup reading to `ひたい`（suppressing stale pitch markers）, while adding underlines, phrase groups, and validated word meanings without triggering a second refresh

#### Scenario: Some requests fail
- **WHEN** at least one sentence request succeeds and another fails
- **THEN** one replacement overlay underlines, annotates, and applies reading overrides only for words from successful segments, leaves failed-segment words with local furigana only, and exposes a retryable warning

#### Scenario: Every request fails
- **WHEN** all sentence requests fail, time out, are refused, or return malformed data
- **THEN** the overlay remains local-furigana-only and exposes a retryable warning without rendering invalid groups, meanings, or reading overrides

#### Scenario: Incremental merge preserves cached segments and reading overrides
- **WHEN** incremental analysis produces fresh results for some segments and reuses cached results for others
- **THEN** the replacement overlay SHALL contain underlines, groups, and validated reading overrides from both fresh and cached segments, and SHALL NOT clear the cached segments
