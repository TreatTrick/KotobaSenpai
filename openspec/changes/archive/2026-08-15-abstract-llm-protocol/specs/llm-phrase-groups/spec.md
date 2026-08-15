## MODIFIED Requirements

### Requirement: Analyze grounded sentence segments with an LLM
The system SHALL submit one locally segmented OCR sentence segment at a time to a provider-neutral phrase analysis port. The request SHALL include the segment text, stable token references with UniDic metadata, and locally resolved continuous JMdict span summaries. The request SHALL NOT include screenshots, window coordinates, window titles, or API keys. The provider transport SHALL be wire-protocol-pluggable: the same semantic request SHALL be deliverable over OpenAI Chat Completions, Anthropic Messages, or OpenAI Responses wire formats, selected by configuration, without changing the port contract.

#### Scenario: Build a grounded phrase request
- **WHEN** a sentence segment has UniDic tokens and local continuous spans
- **THEN** the analyzer request contains the original segment text, unique token IDs, each token's surface/lemma/reading/POS/conjugation metadata, and the local span summaries

#### Scenario: Do not send screenshot data
- **WHEN** phrase analysis is requested for OCR output
- **THEN** the provider payload contains no image bytes, screen rectangles, target window title, or API key

#### Scenario: Deliver the request over any supported protocol
- **WHEN** the configured protocol is OpenAI Chat Completions, Anthropic Messages, or OpenAI Responses
- **THEN** the same semantic request is serialized into that protocol's wire envelope and delivered to that protocol's endpoint path