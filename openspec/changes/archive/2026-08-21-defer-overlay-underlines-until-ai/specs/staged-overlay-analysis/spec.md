## ADDED Requirements

### Requirement: Stage local furigana before optional analysis
The application SHALL publish a local overlay containing UniDic-resolved words and furigana immediately after local grouping when phrase analysis is enabled, without waiting for provider I/O. The staged overlay SHALL contain no underline elements or LLM meanings.

#### Scenario: Initial overlay appears before a slow provider
- **WHEN** local recognition and grouping finish and an enabled provider has not returned yet
- **THEN** the overlay is visible with furigana for eligible words and without word underlines or LLM meanings

#### Scenario: Provider analysis is disabled
- **WHEN** phrase analysis is disabled by settings
- **THEN** the application publishes the existing local overlay with furigana and underlines without a staged delay

### Requirement: Publish one post-analysis overlay refresh
After all sentence requests in an enabled analysis batch complete, the application SHALL replace the staged overlay exactly once. The replacement SHALL add underlines and validated meanings for successful sentence segments, preserve furigana for every local word, and retain a warning for failed segments.

#### Scenario: All requests succeed
- **WHEN** every sentence request completes successfully
- **THEN** one replacement overlay contains furigana, underlines, phrase groups, and validated word meanings

#### Scenario: Some requests fail
- **WHEN** at least one sentence request succeeds and another fails
- **THEN** one replacement overlay underlines and annotates only words from successful segments, leaves failed-segment words with furigana only, and exposes a retryable warning

#### Scenario: Every request fails
- **WHEN** all sentence requests fail, time out, are refused, or return malformed data
- **THEN** the overlay remains furigana-only and exposes a retryable warning without rendering invalid groups or meanings

### Requirement: Superseded recognition cannot publish
A recognition run SHALL NOT publish either its staged or post-analysis overlay after a newer recognition run or hide operation supersedes it.

#### Scenario: Older provider response arrives late
- **WHEN** a second recognition starts before the first provider batch completes
- **THEN** the first run's late result does not replace the second run's overlay

#### Scenario: Hide supersedes pending analysis
- **WHEN** the overlay is hidden while provider analysis is pending
- **THEN** the pending run does not show an overlay after completion
