# llm-result-cache Specification

## Purpose

Reuse successful sentence-level LLM analysis within recognition sessions using a bounded in-memory cache.

## Requirements

### Requirement: 句子级 LLM 结果内存缓存
系统 SHALL 在 `PhraseAnalysisOrchestrator` 层维护一个基于内存的 LLM 分析结果缓存，以句子文本（`SegmentText`）为 key，缓存成功完成的 `PhraseAnalysisResult`。缓存 SHALL NOT 持久化到磁盘，仅在内存中存在。

#### Scenario: 缓存成功结果
- **WHEN** 某句子分段的 LLM 分析成功完成（`Succeeded == true`）
- **THEN** 该分段的 `SegmentText` → `PhraseAnalysisResult` 映射 SHALL 被写入缓存

#### Scenario: 不缓存失败结果
- **WHEN** 某句子分段的 LLM 分析失败
- **THEN** 该分段的结果 SHALL NOT 被写入缓存，下次请求时仍会重新调用 LLM

#### Scenario: 缓存命中
- **WHEN** 新一轮分析中某分段的 `SegmentText` 与缓存中某条目的 key 完全匹配
- **THEN** 系统 SHALL 直接返回缓存的 `PhraseAnalysisResult`，SHALL NOT 向 LLM 发送请求

#### Scenario: 缓存未命中
- **WHEN** 新一轮分析中某分段的 `SegmentText` 不在缓存中
- **THEN** 系统 SHALL 向 LLM 发送请求，并在成功后将结果写入缓存

#### Scenario: 跨轮次保持缓存
- **WHEN** 第一轮识别缓存了句子 A 的结果，第二轮识别（增量模式）仍然包含句子 A
- **THEN** 第二轮识别 SHALL 直接使用第一轮的缓存结果

### Requirement: 缓存容量上限
缓存 SHALL 设置最大容量上限（默认 50 条），当缓存满时 SHALL 淘汰最早的条目（FIFO）。

#### Scenario: 缓存未满时正常写入
- **WHEN** 缓存条目数未达到上限
- **THEN** 新结果直接写入，不淘汰旧条目

#### Scenario: 缓存满时淘汰最早条目
- **WHEN** 缓存条目数已达到上限
- **THEN** 系统 SHALL 移除最早的条目，为新结果腾出空间

### Requirement: 缓存随新会话重置
当系统执行全变模式识别时，LLM 结果缓存 SHALL 被完全清空。

#### Scenario: 全变模式清空缓存
- **WHEN** 自动检测到文本全变或用户手动触发全新识别
- **THEN** 所有缓存条目 SHALL 被清除

#### Scenario: 增量模式保留缓存
- **WHEN** 系统执行增量模式识别
- **THEN** 缓存 SHALL 不被清空，未变化的句子继续使用缓存
