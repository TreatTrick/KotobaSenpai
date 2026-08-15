## 1. Protocol port

- [x] 1.1 Add `ILlmProtocol` interface (`Path`, `BuildBody`, `ExtractGroupsJson`) in `src/KotobaSenpai.Platform.Windows/Llm/`
- [x] 1.2 Implement `OpenAiChatCompletionsProtocol` (path `/chat/completions`, `messages` envelope + `response_format` json_schema strict, `choices[0].message.content` → JsonElement)
- [x] 1.3 Implement `AnthropicMessagesProtocol` (path `/v1/messages`, `system`+`messages`+`max_tokens` envelope + forced `tools`/`tool_choice` `return_groups`, read `content[].tool_use.input` as JsonElement)
- [x] 1.4 Implement `OpenAiResponsesProtocol` (path `/responses`, `input` envelope + `text.format` json_schema strict, `output[].content[].text` → JsonElement)

## 2. Structured-output schema

- [x] 2.1 Define the shared group array JSON schema (type/modelGroupId/parts/label/meaningZh/grammarZh) used by all three protocols' structured-output declarations
- [x] 2.2 Keep the schema fields aligned with `PhraseGroupValidator` so validated output matches requested shape

## 3. Semantic content extraction

- [x] 3.1 Rename `PhraseRequestBuilder` to `PhrasePromptBuilder`, returning `(systemPrompt, userContent)` and keeping the 16KB size check
- [x] 3.2 Move the OpenAI-specific envelope out of `PhraseRequestBuilder`; keep token-table/span serialization shared in `PhrasePromptBuilder`

## 4. Wire analyzer to the protocol

- [x] 4.1 Split `PhraseResponseParser`: replace `Parse(envelope)` + `ExtractJsonArray` with `ParseGroups(JsonElement)` field validation; keep `MalformedJson` fallback
- [x] 4.2 Update `DeepSeekPhraseAnalyzer` to depend on `ILlmProtocol` + `PhrasePromptBuilder` + `PhraseResponseParser`; call `ExtractGroupsJson` then `ParseGroups`
- [x] 4.3 Update DI in `App.xaml.cs` to register `ILlmProtocol` via a factory keyed on the protocol setting

## 5. Configuration

- [x] 5.1 Add `DeepSeekProtocol` key to `DeepSeekSettingsKeys` with a default of OpenAI Chat Completions
- [x] 5.2 Ensure unknown/empty protocol values fall back to OpenAI Chat Completions (no behavior change for existing users)

## 6. Tests & verification

- [x] 6.1 Add/extend tests for each protocol's `BuildBody` envelope shape (incl. structured-output declaration) and `ExtractGroupsJson` branch (fixed sample envelopes + schema-mismatch fallback)
- [x] 6.2 Keep `PhraseRequestBuilder` size-limit regression on the shared `PhrasePromptBuilder`
- [x] 6.3 Run full test suite; existing OpenAI Chat Completions path must stay green
- [x] 6.4 Manual smoke: switch `DeepSeekProtocol` to anthropic and responses, send one phrase request against the Volcengine ARK endpoints to verify envelopes + parsed groups
