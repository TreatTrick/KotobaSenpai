# llm-phrase-groups Specification

## Purpose
TBD - created by archiving change add-llm-phrase-groups. Update Purpose after archive.
## Requirements
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

### Requirement: Indicate group membership by member-word underline on hover
系统 SHALL NOT 为语法组合组绘制独立的上划线。组的成员关系 SHALL 通过高亮其成员本地合并词的下划线来呈现：当某个组被悬停/选中时，覆盖该组 parts 所引用 token 的本地合并词 SHALL 将其下划线渲染为高亮样式，并显示该组的详情弹窗；组未被悬停时，成员词 SHALL 仅显示普通下划线，不呈现任何组信号。

#### Scenario: Highlight member words on hover
- **WHEN** 一个语法组合组被悬停/选中
- **THEN** 覆盖该组 parts token 的本地合并词下划线被高亮，并显示该组详情弹窗

#### Scenario: No overline drawn
- **WHEN** 存在一个已验证的语法组合组
- **THEN** 任何时刻都不在其 token 上方绘制独立上划线

#### Scenario: Rest shows no group signal
- **WHEN** 没有组被悬停
- **THEN** 成员词仅显示各自普通的本地下划线，不呈现组信号

### Requirement: Segment by sentence-final punctuation and analyze sentences concurrently
系统 SHALL 将 OCR 行按句末标点(`。！？…‥．`)或大段落间距断开成句段;**不因阅读顺序不可靠而断开**。每个句段作为一个独立连续的文本块,本地分词与 LLM 请求使用同一句段边界。系统 SHALL 对每个句段并发发起一个 LLM 请求(复用既有的并发上限),每个请求 SHALL 仅携带该句段的 token 表。`」` 不单独作为断句依据(`。」`/`？」` 已含句末标点)。

#### Scenario: Merge a wrapped sentence across lines
- **WHEN** 一句长句被折行,行1 末尾无句末标点、行2 继续
- **THEN** 两行并为一个句段,一次分词、一次 LLM 请求,跨行词在段内完整

#### Scenario: Break only at sentence-final punctuation or a paragraph gap
- **WHEN** 一行以句号结尾,或两行间隔超过段落间距
- **THEN** 该处断开为两个句段,各自独立分词与请求

#### Scenario: An embedded closing quote does not break a sentence
- **WHEN** 一句内嵌引用的中途出现 `」`(如 `「彼は「行こう」と`),但非句末
- **THEN** 该行不因 `」` 单独断开,继续并入当前句段

#### Scenario: Each request carries only its segment's tokens
- **WHEN** 一个句段发起 LLM 请求
- **THEN** 该请求的 token 表只含本句段的 token,不含其他句段

