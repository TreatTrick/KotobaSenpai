## MODIFIED Requirements

### Requirement: Preserve local fallback
The system SHALL complete and expose local UniDic/JMdict words and continuous spans even when phrase analysis is unavailable. Missing API key, cancellation, timeout, transport failure, provider refusal, malformed JSON, and an all-invalid response SHALL produce a retryable warning state without crashing. When analysis is enabled, local words SHALL remain visible as furigana during the provider wait; only successful sentence segments SHALL receive underlines and provider-derived presentation after the complete batch. When analysis is disabled, the existing local underlines and readings SHALL be shown.

#### Scenario: Provider timeout
- **WHEN** the phrase provider times out
- **THEN** the local overlay remains visible with furigana only, no invalid phrase group is rendered, and the UI exposes a retryable phrase-analysis failure state

#### Scenario: Missing API key
- **WHEN** no provider key is configured while phrase analysis is enabled
- **THEN** the application skips the provider call, keeps local furigana visible without underlines, and reports that phrase analysis requires configuration

#### Scenario: Disabled provider analysis
- **WHEN** phrase analysis is disabled
- **THEN** the application keeps the existing local words, furigana, and underlines without waiting for a provider

#### Scenario: Partial sentence failure
- **WHEN** one sentence request succeeds and another request fails
- **THEN** the final overlay underlines only words from the successful sentence, keeps furigana for both sentences, and reports a retryable warning
