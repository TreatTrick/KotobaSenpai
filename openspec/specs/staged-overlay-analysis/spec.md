# staged-overlay-analysis Specification

## Purpose
TBD - created by archiving change defer-overlay-underlines-until-ai. Update Purpose after archive.
## Requirements
### Requirement: Stage local furigana before optional analysis
For initial or full-change recognition, the application SHALL publish a local overlay containing UniDic-resolved words and furigana immediately after local grouping when phrase analysis is enabled, without waiting for provider I/O. The staged overlay SHALL contain no underline elements or LLM meanings. Publication SHALL update the saved session while target visibility rules determine whether it is displayed; temporary hiding due to target invisibility, minimization, or an occlusion union strictly greater than 30% of the full client area SHALL retain the latest completed result for restoration. 在增量识别场景中，系统 SHALL 在增量模式完成前保持上一轮 overlay 不变（未变化部分的下划线、短语组、词义均保留），增量模式完成后再刷新。

#### Scenario: Initial overlay appears before a slow provider
- **WHEN** initial or full-change local recognition and grouping finish, an enabled provider has not returned yet, and the target is renderable
- **THEN** the overlay is visible with furigana for eligible words and without word underlines or LLM meanings

#### Scenario: Provider analysis is disabled
- **WHEN** phrase analysis is disabled by settings
- **THEN** the application publishes the existing local overlay with furigana and underlines without a staged delay

#### Scenario: Incremental recognition preserves prior overlay during analysis
- **WHEN** incremental recognition is in progress (some segments reuse cached LLM results, others need fresh analysis)
- **THEN** the prior overlay session SHALL NOT be cleared; unchanged word underlines, phrase groups, and word meanings SHALL remain visible while the target is renderable until the incremental cycle completes

#### Scenario: Incremental cycle completion refreshes overlay once
- **WHEN** all incremental segments (both cached and fresh) are processed
- **THEN** the application SHALL issue exactly one overlay refresh containing the merged result

### Requirement: Publish one post-analysis overlay refresh
After all sentence requests in an enabled analysis batch complete, the application SHALL replace the staged overlay exactly once. The replacement SHALL add underlines and validated meanings for successful sentence segments, preserve furigana for every local word, and retain a warning for failed segments. 在增量模式下，替换 SHALL 保留上一轮缓存命中段的下划线和短语组，仅新增或更新有变化的段。

#### Scenario: All requests succeed
- **WHEN** every sentence request completes successfully
- **THEN** one replacement overlay contains furigana, underlines, phrase groups, and validated word meanings

#### Scenario: Some requests fail
- **WHEN** at least one sentence request succeeds and another fails
- **THEN** one replacement overlay underlines and annotates only words from successful segments, leaves failed-segment words with furigana only, and exposes a retryable warning

#### Scenario: Every request fails
- **WHEN** all sentence requests fail, time out, are refused, or return malformed data
- **THEN** the overlay remains furigana-only and exposes a retryable warning without rendering invalid groups or meanings

#### Scenario: Incremental merge preserves cached segments
- **WHEN** incremental analysis produces fresh results for some segments and reuses cached results for others
- **THEN** the replacement overlay SHALL contain underlines/groups from both fresh and cached segments, SHALL NOT clear the cached segments

### Requirement: Superseded recognition cannot publish
A recognition run SHALL NOT publish either its staged or post-analysis overlay after a newer recognition run, target switch, or explicit user request to end or hide the recognition session supersedes it. Temporary visibility hiding due to target invisibility, minimization, or occlusion SHALL NOT supersede pending analysis; a still-current run SHALL save its latest completed result without reopening a hidden overlay.

#### Scenario: Older provider response arrives late
- **WHEN** a second recognition starts before the first provider batch completes
- **THEN** the first run's late result does not replace the second run's overlay

#### Scenario: Explicit hide supersedes pending analysis
- **WHEN** the user explicitly hides or ends the recognition session while provider analysis is pending
- **THEN** the pending run does not publish a saved result or show an overlay after completion

#### Scenario: Temporary visibility hide retains pending analysis
- **WHEN** the target becomes invisible, minimized, or occluded beyond 30% of its full client area while the current provider analysis is pending
- **THEN** completed analysis updates the saved session without showing the hidden overlay; restoration uses the latest completed result when the target is visible, restored, and at most 30% occluded, without starting OCR or LLM itself

#### Scenario: Incremental cycle superseded by a full-change cycle
- **WHEN** an incremental recognition cycle is in progress and a full-change detection triggers a new full recognition
- **THEN** the incremental cycle's result SHALL NOT publish; only the full recognition's result SHALL be rendered
