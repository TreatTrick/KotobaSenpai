## ADDED Requirements

### Requirement: Provide a pluggable provider-wire-protocol port
The system SHALL expose a protocol port that, given a canonical phrase-analysis request and a model name, produces the provider-specific request envelope, the provider-specific request path, and extracts the structured group array from the provider-specific response envelope as a JSON element. Each protocol SHALL declare its native structured-output mechanism so the provider is constrained to return schema-compliant JSON. The port SHALL be implemented independently for OpenAI Chat Completions, Anthropic Messages, and OpenAI Responses wire formats.

#### Scenario: Select the OpenAI Chat Completions protocol
- **WHEN** the configured protocol is OpenAI Chat Completions
- **THEN** the request posts to the `/chat/completions` path with an OpenAI `messages` envelope plus a strict `response_format` JSON schema, and the group array is read from `choices[0].message.content`

#### Scenario: Select the Anthropic Messages protocol
- **WHEN** the configured protocol is Anthropic Messages
- **THEN** the request posts to the `/v1/messages` path with an Anthropic `system`+`messages`+`max_tokens` envelope plus a forced `tool_use`, and the group array is read from `content[].tool_use.input`

#### Scenario: Select the OpenAI Responses protocol
- **WHEN** the configured protocol is OpenAI Responses
- **THEN** the request posts to the `/responses` path with an OpenAI `input` envelope plus a strict `text.format` JSON schema, and the group array is read from the `output` array's text content

### Requirement: Select the protocol by configuration
The system SHALL select the active wire protocol from a BYOK settings key, defaulting to OpenAI Chat Completions when unset. Changing the key SHALL change which protocol implementation the provider analyzer uses without changing the phrase-analysis port contract.

#### Scenario: Default to OpenAI Chat Completions
- **WHEN** no protocol is configured
- **THEN** the analyzer uses the OpenAI Chat Completions protocol

#### Scenario: Switch protocol by configuration
- **WHEN** the protocol setting is changed to Anthropic Messages
- **THEN** subsequent phrase requests use the Anthropic Messages path and envelope

### Requirement: Preserve semantic content across protocols
All three protocols SHALL transmit the same canonical semantic content — the segment text, stable token references with UniDic metadata, and locally resolved continuous span summaries — and SHALL produce the same group array for validation. The protocol abstraction SHALL add no semantics beyond wire-format translation.

#### Scenario: Same payload across protocols
- **WHEN** the same phrase request is sent through any of the three protocols
- **THEN** each protocol's structured group array is validated by the same group parser and yields the same groups

#### Scenario: Fall back when a provider returns non-JSON
- **WHEN** a provider ignores its structured-output declaration and returns text that is not a JSON array
- **THEN** the group parser records a malformed-JSON warning and local words/spans remain available