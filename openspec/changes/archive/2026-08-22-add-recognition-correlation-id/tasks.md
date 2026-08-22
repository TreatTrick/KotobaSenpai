## 1. Core 关联契约

- [x] 1.1 为 `IWindowWordRecognizer.RecognizeAsync` 增加由应用管理的 `Guid` run ID 参数，并更新所有实现和测试替身，确保 OCR 在 capture 开始前就收到该 ID。
- [x] 1.2 在 `PhraseAnalysisRequest` 中增加作为本地元数据的 run ID，更新 `PhraseAnalysisOrchestrator.AnalyzeAsync` 和 request 构建逻辑，使每个 sentence segment 都保留该 ID；prompt/protocol 序列化仍只包含语义字段。
- [x] 1.3 扩展 `IDiagnosticReporter.RecordTokens`、`RecordPhraseAnalysis` 和 `RecordLlmExchange`，使它们接收 run ID，同时保留 LLM segment ID；更新仓库内所有实现和 fake。

## 2. 识别管线传播

- [x] 2.1 在 `WordOverlayApplicationService.RecognizeAndShowAsync` 开始时生成一个 `Guid`，并在取消和部分失败路径中将同一个值传递给 OCR、token 诊断、phrase 诊断和 phrase orchestrator。
- [x] 2.2 更新 `MeikiOcrWordRecognizer` 的诊断 capture，使截图和 OCR 文本文件使用传入的 run ID，同时保留现有 capture、OCR 输出、诊断开关和保留策略。
- [x] 2.3 更新 `PhraseAnalysisOrchestrator` 和 `LlmPhraseAnalyzer`，使每次 LLM exchange 使用 request 中的 run ID 写入诊断；segment ID 继续用于单个 request 的关联，provider payload 保持不变。
- [x] 2.4 在最终 analysis result 产生后使用 run ID 记录 phrase-analysis 诊断，覆盖 no-key、取消、provider failure 和空 request 等结果。

## 3. 关联诊断文件

- [x] 3.1 更新 `FileDiagnosticReporter`，让截图、OCR、token、phrase 以及 LLM request/response 文件名使用统一的 `N` 格式 run ID 组件。
- [x] 3.2 让 LLM request/response 文件通过 run ID、经过安全处理的 segment ID 和 sequence 成对关联，并保留所有诊断文件名组件的安全处理。
- [x] 3.3 保留现有 `%LocalAppData%/KotobaSenpai/diag/` 位置、`DiagEnabled` 开关、API key 排除、UTF-8/JSON 格式和按前缀清理的保留上限。

## 4. 测试与验证

- [x] 4.1 增加 Core/application-service 测试，验证同一个生成的 run ID 会传递到 OCR、token 诊断、phrase 诊断和所有 phrase request，并验证连续或重叠识别获得不同 ID。
- [x] 4.2 增加 Windows OCR 和 LLM 测试，验证 OCR 文件名、LLM request/response 配对、segment ID 保留以及 provider payload 不包含 run ID。
- [x] 4.3 增加 App diagnostic reporter 测试，验证关联文件名、文件名安全处理、诊断关闭、API key 排除和原有按前缀保留行为。
- [x] 4.4 运行格式化/build、Core/App/Platform.Windows 重点测试、完整 solution 测试，以及 `openspec status --change add-recognition-correlation-id`，确认该变更可以进入实施阶段。
