# staged-overlay-analysis Specification

## Purpose
TBD - created by archiving change defer-overlay-underlines-until-ai. Update Purpose after archive.
## Requirements
### Requirement: Stage local furigana before cloud analysis
For initial or full-change recognition, the application SHALL publish a local overlay containing UniDic-resolved words and furigana immediately after local grouping, without waiting for provider I/O. Cloud phrase analysis SHALL be required; historical enable settings SHALL NOT bypass the analysis workflow. The staged overlay SHALL contain no underline elements or LLM meanings. Publication SHALL update the saved session while target visibility rules determine whether it is displayed; temporary hiding due to target invisibility, minimization, or an occlusion union strictly greater than 30% of the full client area SHALL retain the latest completed result for restoration. 在增量识别场景中，系统 SHALL 在增量模式完成前保持上一轮 overlay 不变（未变化部分的下划线、短语组、词义均保留），增量模式完成后再刷新。

#### Scenario: Initial overlay appears before a slow provider
- **WHEN** initial or full-change local recognition and grouping finish, the provider has not returned yet, and the target is renderable
- **THEN** the overlay is visible with furigana for eligible words and without word underlines or LLM meanings

#### Scenario: Historical disabled-analysis setting does not bypass analysis
- **WHEN** a historical setting disables phrase analysis
- **THEN** the application still enters cloud analysis and publishes only local furigana while waiting, without successful-analysis underlines

#### Scenario: Incremental recognition preserves prior overlay during analysis
- **WHEN** incremental recognition is in progress (some segments reuse cached LLM results, others need fresh analysis)
- **THEN** the prior overlay session SHALL NOT be cleared; unchanged word underlines, phrase groups, and word meanings SHALL remain visible while the target is renderable until the incremental cycle completes

#### Scenario: Incremental cycle completion refreshes overlay once
- **WHEN** all incremental segments (both cached and fresh) are processed
- **THEN** the application SHALL issue exactly one overlay refresh containing the merged result

### Requirement: Publish one post-analysis overlay refresh
After all sentence requests in an analysis batch complete, the application SHALL replace the staged overlay exactly once. The replacement SHALL add underlines and validated meanings for successful sentence segments, preserve furigana for every local word, and retain a warning for failed segments. 在增量模式下，替换 SHALL 保留上一轮缓存命中段的下划线和短语组，仅新增或更新有变化的段。

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

### Requirement: 保存语言不立即替换已有显示

保存释义语言时，系统 SHALL 保留已经显示或因目标临时隐藏而保存的词义、短语组和弹窗状态，下一次识别时按现有流程更新。语言设置 SHALL 不主动触发 OCR、模型请求或新的覆盖层发布；已有目标可见性、增量合并和识别发布规则继续适用。

#### Scenario: 保存后保留当前结果
- **WHEN** 当前显示中文释义，用户保存“法语”但尚未进行新识别
- **THEN** 当前显示继续保留，之后启动的分析使用法语设置，保存本身不触发请求或覆盖层重建

### Requirement: 换语言后下一次识别不被相同原文跳过

有效语言变化时，系统 SHALL 使仅按原文判断“无需分析”的旧状态失效。下一次真正启动的手动或自动识别 SHALL 使用新语言，即使其 OCR 原文与上次完全相同；没有启动新识别时 SHALL 不主动发起 OCR 或模型调用。

#### Scenario: 原文相同仍按新语言分析
- **WHEN** 用户保存法语后，下一次识别得到与上次完全相同的日语原文
- **THEN** 系统不因原文相同而跳过分析，按法语构建新请求
