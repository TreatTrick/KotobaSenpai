## Context

`WordOverlayApplicationService` 是一次用户触发识别的入口。目前它只递增内存中的 generation，用于防止过期 overlay 覆盖新结果；各个诊断生产者则独立生成基于时间戳的文件名。OCR 写入截图和 OCR 文本，应用服务写入 token 和 phrase 诊断，LLM adapter 为每个 sentence segment 写入一对请求/响应文件。这些标识之间没有关联，因此无法按识别运行查看诊断目录。

这次变更会跨越 Core 契约、Windows OCR/LLM adapter 和 App 诊断写入器。识别 ID 必须在 capture/OCR 开始前就可用，在本地 UniDic 分组和并发 phrase 请求期间保持不变，并且只保留在应用内部，因为它只是诊断关联键。

## Goals / Non-Goals

**Goals：**

- 每次 `RecognizeAndShowAsync` 调用只生成一个由应用管理的 ID。
- 将该 ID 传递到 OCR 诊断、本地 token 诊断、phrase 诊断以及这次识别产生的每一次 LLM exchange。
- 让每个诊断文件名都携带相同的识别 ID，并保留足够的 segment 信息来区分同一次识别内的多个 LLM 请求。
- 保持请求/响应对可匹配、文件保留数量有界，以及现有诊断开关行为不变。
- 让 ID 在测试边界中显式传递，使测试无需依赖系统时钟即可验证关联关系。

**Non-Goals：**

- 不向 LLM provider 发送识别 ID、截图、窗口元数据或仅用于诊断的元数据。
- 不替换 sentence segment ID、token ID 或应用管理的 phrase group ID。
- 不改变 overlay 生成语义、取消策略、设置项或诊断目录位置。
- 不引入持久化数据库或跨进程 ID 注册表。

## Decisions

### 每次识别使用一个 GUID

应用服务在每次识别开始时调用 `Guid.NewGuid()`，然后将该值传递给 OCR recognizer 和 phrase-analysis orchestrator。文件名使用 `N` 格式（`32` 位小写十六进制字符）表示该值。GUID 可以直接使用现有平台能力，能够避免时间戳冲突，便于测试断言，也不需要为诊断专门增加新的 value object 抽象。

**考虑过的替代方案：** 使用时间戳作为识别 ID。该方案被否决，因为并发识别或同一毫秒内的快速连续识别可能得到相同时间戳，而且时间戳作为关联键的表达力较弱。

### 通过现有请求/端口边界传递 ID

将识别 ID 显式加入识别和 phrase-analysis 契约，而不是使用 ambient state 或静态的 current-run 变量：

- `IWindowWordRecognizer.RecognizeAsync` 接收识别 ID，使 OCR 可以命名截图和 OCR 文本文件。
- `PhraseAnalysisRequest` 将识别 ID 作为本地元数据携带；prompt builder 和 protocol serializer 仍然只选择语义请求字段，因此 ID 不会被序列化给 provider。
- `PhraseAnalysisOrchestrator.AnalyzeAsync` 接收识别 ID，并将它放入自己创建的每个 request。
- `IDiagnosticReporter` 接收识别 ID，用于记录 token、phrase 和 LLM exchange。

应用服务在整个调用期间使用同一个值，包括 staged overlay 发布和并发 sentence 请求。本地分组仍然保持纯逻辑，因为它本身不写诊断；分组完成后，在 token 诊断边界传入识别 ID。

**考虑过的替代方案：** 将当前 ID 存放在 singleton diagnostic reporter 中。该方案被否决，因为并发识别或延迟返回的 provider 结果可能覆盖 ambient state，导致文件关联到错误的识别。

### 使用公共文件名组件，同时保留诊断类型前缀

所有诊断文件名都在 artifact 前缀后立即包含识别 ID：

- `frame-{runId}.png`
- `ocr-{runId}.txt`
- `tokens-{runId}.txt`
- `phrase-{runId}.txt`
- `llm-req-{runId}-{segmentId}-{sequence}.json`
- `llm-resp-{runId}-{segmentId}-{sequence}.json`

LLM segment ID 仍然是诊断文件名中的细分信息，而不是第二个识别身份。现有 sequence counter 继续用于防止并发调用在 sanitized segment ID 相同时发生冲突。同一次 LLM exchange 的两个文件使用完全相同的 run、segment 和 sequence 组件。

文件保留仍然按 artifact 前缀分别执行。新文件名仍兼容现有的前缀匹配方式，现有的每个前缀最大保留数量不变。

**考虑过的替代方案：** 为每次识别创建一个以 ID 命名的子目录。该方案被否决，因为当前需求是可以直接从诊断目录中的文件名识别关联文件，而且子目录方案会要求改变目录扫描和保留策略。

### 将仅用于诊断的元数据排除在 provider payload 之外

识别 ID 存放在 canonical `PhraseAnalysisRequest` 中，用于本地路由到诊断 reporter；但是 protocol 实现只从 `SegmentText`、`Tokens` 和 `LocalSpans` 构建 provider payload。测试必须断言序列化后的请求体不包含识别 ID。这样可以保持现有 provider 契约不变，也不会向外部服务暴露内部文件系统关联键。

## Risks / Trade-offs

- [修改 Core 接口会影响现有 fake 和 platform adapter] -> 同步更新仓库内所有实现和测试替身，并在重点测试中使用确定性的 ID。
- [LLM 请求可能在新识别开始后才返回] -> 现有 generation 检查继续保护 overlay 发布；显式识别 ID 可以确保延迟产生的诊断文件仍归属于真正创建它的识别。
- [目录中会保留只带时间戳的旧文件] -> 保持现有按前缀清理的行为，并只对新写入文件使用新命名；不做破坏性的旧文件重命名或迁移。
- [sanitized segment ID 可能冲突] -> 保留 LLM 文件对的 sequence 后缀，并使用 run GUID 提供跨识别唯一性。
- [诊断写入失败可能影响识别流程] -> 保持现有尽力写入和清理行为；本次关联变更不把诊断升级为识别流程的必需依赖。

## Migration Plan

1. 在 Core 识别/phrase request 和诊断契约中加入识别 ID，然后同步更新所有实现和测试替身。
2. 在 `WordOverlayApplicationService` 中生成一个 GUID，并将它传递到 OCR 和 phrase analysis。
3. 更新所有诊断文件名生成逻辑，同时保留按前缀清理的策略。
4. 增加契约传播、文件名、隐私和并发方面的测试。
5. 运行 Core/App/Windows 重点测试以及完整 solution 测试集。

回滚方式是回退代码。已有诊断文件仍然可读；不需要设置、provider 契约或持久化应用数据迁移。

## Open Questions

无。识别 ID 格式和文件名布局已经在本设计中确定，实施和测试不需要再做产品决策。
