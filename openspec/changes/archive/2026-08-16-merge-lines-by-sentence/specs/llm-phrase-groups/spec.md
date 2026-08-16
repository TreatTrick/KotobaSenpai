## ADDED Requirements

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