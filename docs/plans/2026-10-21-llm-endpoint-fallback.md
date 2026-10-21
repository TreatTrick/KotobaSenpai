# LLM Endpoint Fallback Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Try the configured analysis URL unchanged, fall back only after HTTP 404/405, and use JSON object output for all Chat Completions requests.

**Architecture:** Keep candidate generation in `LlmEndpointHelper` and the bounded HTTP loop in `LlmPhraseAnalyzer`. Keep protocol-specific authentication and parsing in the existing protocol implementations. Chat supplies a JSON example in its prompt instead of a wire-level JSON Schema; existing parsing and semantic validation remain authoritative.

**Tech Stack:** C#/.NET 10, HttpClient, WPF resources, xUnit, OpenSpec.

---

### Task 1: Add failing request-contract tests

**Files:**
- Modify: `tests/KotobaSenpai.Platform.Windows.Tests/LlmPhraseAnalyzerTests.cs`
- Create: `tests/KotobaSenpai.Platform.Windows.Tests/LlmEndpointFallbackTests.cs`

1. Replace the Chat strict-schema assertion with `response_format.type == "json_object"`, a complete JSON example, and a bounded output token assertion.
2. Exercise the real analyzer with a recording HTTP handler. Assert that the first POST preserves a complete URL and query, successful calls stop immediately, and only 404/405 advance candidates.
3. Cover Chat/Responses versioned and unversioned paths; Anthropic standard and `/anthropic` paths; existing `/v1`, custom prefixes, complete endpoint URLs, duplicate elimination, cancellation, and exhausted candidates.
4. Assert that HTTP error exchanges are recorded without reflected API keys and that final errors include the HTTP status and attempt count.
5. Run `dotnet test tests/KotobaSenpai.Platform.Windows.Tests/KotobaSenpai.Platform.Windows.Tests.csproj --filter "FullyQualifiedName~LlmEndpointFallbackTests|FullyQualifiedName~LlmProtocolTests|FullyQualifiedName~LlmPhraseAnalyzerTests" --verbosity minimal`; confirm failures describe the missing behavior.

### Task 2: Implement deterministic URL fallback

**Files:**
- Modify: `src/KotobaSenpai.Platform.Windows/Llm/LlmEndpointHelper.cs`
- Modify: `src/KotobaSenpai.Platform.Windows/Llm/LlmPhraseAnalyzer.cs`

1. Generate ordered URI candidates beginning with the configured URI. Preserve query parameters and custom prefixes; recognize already-complete protocol paths.
2. Append versioned and unversioned protocol paths without duplicate `/v1` segments. For Anthropic, also append `/anthropic/v1/messages`, avoiding duplicate `/anthropic` segments.
3. Deduplicate by URI equality and send a fresh authenticated POST/content instance per candidate.
4. Continue only on 404/405. Stop on success, other HTTP errors, malformed successful responses, timeout, transport failure, or caller cancellation.
5. Record failed exchanges and log the protocol, target path, HTTP status, and duration. Redact reflected API keys. Report `Refused` with the final status and number of attempted URLs when candidates are exhausted.
6. Re-run the targeted tests and verify they pass.

### Task 3: Use Chat JSON object mode and synchronize documentation

**Files:**
- Modify: `src/KotobaSenpai.Platform.Windows/Llm/OpenAiChatCompletionsProtocol.cs`
- Modify: `src/KotobaSenpai.Platform.Windows/Llm/ILlmProtocol.cs`
- Modify: `src/KotobaSenpai.App/Resources/Strings.resx`
- Modify: `src/KotobaSenpai.App/Resources/Strings.zh-CN.resx`
- Modify: `openspec/changes/add-llm-settings-and-model-discovery/{proposal,design,tasks}.md`
- Modify: `openspec/changes/add-llm-settings-and-model-discovery/specs/llm-protocol-abstraction/spec.md`
- Modify: `openspec/changes/add-llm-settings-and-model-discovery/specs/llm-settings/spec.md`

1. Always send `response_format: { "type": "json_object" }` and `max_tokens: 4096` for Chat. Include a JSON example with the existing group and word fields in the system message; do not send `json_schema` or `strict` for Chat.
2. Update localized Chat instructions to refer to the supplied JSON structure and clarify that the address field accepts a base URL or a complete analysis URL.
3. Keep Anthropic tool schemas and Responses `text.format` unchanged. Keep model-catalog GET discovery outside this POST fallback change.
4. Synchronize the accepted behavior into the existing OpenSpec change.
5. Run `dotnet test KotobaSenpai.slnx --verbosity minimal`, `openspec validate add-llm-settings-and-model-discovery --strict`, and `git diff --check`.
6. When available, use an isolated in-memory configuration and temporary diagnostics directory to verify the three protocols against the configured provider without changing user settings.

### Verification results

- The initial targeted run failed 28 cases for the expected missing raw-URL behavior, path fallback, diagnostics, and Chat JSON object format. After implementation, all 56 targeted tests passed.
- `dotnet test KotobaSenpai.slnx --verbosity minimal`: 466 tests passed.
- `openspec validate add-llm-settings-and-model-discovery --strict`: valid.
- Live requests against the configured DeepSeek origin first received HTTP 404 at the unchanged URL. Chat then succeeded at `/v1/chat/completions`, and Responses at `/v1/responses`; both returned validated drawable groups.
- Anthropic received HTTP 404 at the origin and `/v1/messages`, then HTTP 200 at `/anthropic/v1/messages`. In two checks, the provider nested the output under `tool_use.input.input` rather than placing `groups` directly under `tool_use.input`. The existing parser correctly returned `MalformedJson` without trying another address. This provider-output limitation remains visible in diagnostics and is outside the agreed address fallback and Chat output-format changes.
