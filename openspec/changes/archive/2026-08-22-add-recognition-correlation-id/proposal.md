## Why

当前一次用户触发的识别会产生多个诊断文件，但这些文件分别使用时间戳、文件前缀和 segment ID 命名，彼此没有统一关联关系。因此，OCR 截图与文本、UniDic 分词结果、短语分析结果以及 LLM 请求/响应无法可靠地判断是否属于同一次识别。需要从 OCR 开始，为整条识别流程创建一个由应用管理的唯一识别 ID，并贯穿到可选的 AI 分析阶段，使所有诊断文件可以按一次识别完整查看。

## What Changes

- 在每次识别流程开始时创建一个唯一识别 ID，并将它传递到 OCR、本地 UniDic/token 分组、短语分析编排和 LLM 分析。
- 扩展诊断记录契约，使每个产生诊断文件的操作都接收识别 ID，同时保留 sentence segment ID 用于一次识别内部的请求关联。
- 同一次识别生成的所有诊断文件名都包含相同的、经过文件名安全处理的识别 ID，包括截图、OCR 文本、token 结果、phrase 结果以及 LLM 请求/响应文件。
- 识别 ID 只作为应用内部和诊断元数据使用；不得加入 provider 请求体，也不得替代 segment ID 或 token ID。
- 保留现有的诊断开关、文件保留/清理策略、API key 脱敏行为，以及可选 phrase analysis 不可用时的现有流程行为。

## Capabilities

### New Capabilities

- `recognition-diagnostic-correlation`：定义识别运行 ID 的生命周期、在本地分析和 provider 分析阶段之间的传播方式，以及诊断文件的确定性关联方式。

### Modified Capabilities

<!-- 没有改变现有面向用户的能力要求；跨模块的诊断关联契约由新能力单独定义。 -->

## Impact

- Core 模型、服务契约和 `WordOverlayApplicationService` 将在识别管线中携带识别 ID。
- `MeikiOcrWordRecognizer`、`PhraseAnalysisOrchestrator` 和 `LlmPhraseAnalyzer` 将在诊断边界接收并转发识别 ID。
- `IDiagnosticReporter` 和 `FileDiagnosticReporter` 的方法签名以及所有诊断文件的命名方式将发生变化。
- Core、App 和 Windows 测试需要增加针对全流程统一 ID、并发/连续识别 ID 唯一性、文件名安全处理和保留策略的测试。
- 不需要修改外部 API、provider 请求协议、设置迁移或持久化领域数据。
