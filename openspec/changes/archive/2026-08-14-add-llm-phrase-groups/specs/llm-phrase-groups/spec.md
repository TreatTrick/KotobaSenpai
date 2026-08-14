## ADDED Requirements

### Requirement: Analyze grounded sentence segments with an LLM
The system SHALL submit one locally segmented OCR sentence segment at a time to a provider-neutral phrase analysis port. The request SHALL include the segment text, stable token references with UniDic metadata, and locally resolved continuous JMdict span summaries. The request SHALL NOT include screenshots, window coordinates, window titles, or API keys.

#### Scenario: Build a grounded phrase request
- **WHEN** a sentence segment has UniDic tokens and local continuous spans
- **THEN** the analyzer request contains the original segment text, unique token IDs, each token's surface/lemma/reading/POS/conjugation metadata, and the local span summaries

#### Scenario: Do not send screenshot data
- **WHEN** phrase analysis is requested for OCR output
- **THEN** the provider payload contains no image bytes, screen rectangles, target window title, or API key

### Requirement: Return only combination groups
The LLM response SHALL contain zero or more meaningful combination groups and SHALL NOT be required to repeat ordinary tokens or continuous spans already supplied by local analysis. The response SHALL include a request-local model group ID, group type, one or more parts, a label, a Chinese meaning, and a Chinese grammar explanation.

#### Scenario: Return a non-continuous group
- **WHEN** the segment contains a meaningful expression whose parts are separated by other tokens
- **THEN** the response contains one group with multiple parts, a label, a Chinese meaning, and a Chinese grammar explanation

#### Scenario: Return no combination
- **WHEN** the segment contains no combination worth explaining
- **THEN** the response contains an empty group list and local token/span results remain usable

### Requirement: Reference existing contiguous token parts
Every group part SHALL be a non-empty, ordered list of token IDs from the same request segment. Token IDs within one part SHALL represent a contiguous token sequence. A group SHALL contain one or more parts, and a token SHALL NOT be repeated within the same group. Different groups MAY reference overlapping tokens.

#### Scenario: Accept a cross-line continuous part
- **WHEN** a meaningful word is split across two accepted adjacent OCR lines
- **THEN** the group contains one ordered part referencing the tokens across the line boundary and the application treats it as one group

#### Scenario: Accept separated parts
- **WHEN** a grammar pattern has two meaningful token sequences separated by intervening tokens
- **THEN** the group contains separate contiguous parts and does not include the intervening tokens in either part

#### Scenario: Reject an invalid reference
- **WHEN** a group references an unknown token ID, repeats a token within itself, or lists non-contiguous IDs inside a part
- **THEN** the application drops that group and continues processing other valid groups and local results

### Requirement: Bound and validate model output
The system SHALL accept at most eight valid groups per sentence segment. It SHALL validate JSON shape, required fields, field lengths, group ID uniqueness within the response, token ownership, part ordering, and segment ownership before rendering. Invalid groups SHALL NOT abort the whole local recognition result.

#### Scenario: Cap an oversized response
- **WHEN** the provider returns more than eight groups
- **THEN** the application keeps at most the first eight groups in provider order after validation and records a diagnostic warning

#### Scenario: Ignore malformed groups individually
- **WHEN** one group has malformed fields but another group is valid
- **THEN** the valid group is retained, the malformed group is discarded, and local words/spans remain available

### Requirement: Assign application-owned group identity
The provider SHALL return only a request-local model group ID. After validation, the application SHALL assign a unique session group ID and use that ID for all parts, geometry, hover state, and detail presentation.

#### Scenario: Repeated model IDs across requests
- **WHEN** two separate analysis requests both return model group ID `g1`
- **THEN** the application assigns different session group IDs and does not merge their highlights

### Requirement: Preserve local fallback
The system SHALL complete and expose local UniDic/JMdict words and continuous spans even when phrase analysis is unavailable. Missing API key, cancellation, timeout, transport failure, provider refusal, malformed JSON, and an all-invalid response SHALL produce a retryable warning state without crashing or hiding local results.

#### Scenario: Provider timeout
- **WHEN** the phrase provider times out
- **THEN** the local overlay remains visible, no invalid phrase group is rendered, and the UI exposes a retryable phrase-analysis failure state

#### Scenario: Missing API key
- **WHEN** no provider key is configured
- **THEN** the application skips the provider call, keeps local words/spans, and reports that phrase analysis requires configuration
