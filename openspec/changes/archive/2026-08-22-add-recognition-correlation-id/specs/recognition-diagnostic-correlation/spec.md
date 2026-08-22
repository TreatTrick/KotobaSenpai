## ADDED Requirements

### Requirement: 创建并传播唯一识别运行 ID

系统 SHALL 在每次用户触发的识别开始时创建一个且仅一个由应用管理的识别运行 ID，并将同一个 ID 用于该识别的 OCR 阶段、本地 token 诊断阶段、phrase-analysis 阶段以及属于该识别的所有 LLM exchange。不同的识别调用 SHALL 获得不同的 ID，包括彼此重叠或发生在同一时钟毫秒内的调用。

#### Scenario: 一次识别的所有阶段使用同一个 ID

- **WHEN** 一次识别完成截图、完成本地 UniDic/token 分组，并执行一个或多个 phrase-analysis request
- **THEN** OCR、token、phrase 和 LLM 诊断操作都接收到同一个识别运行 ID

#### Scenario: 重叠识别保持 ID 独立

- **WHEN** 第一次识别的 provider request 尚未完成时，第二次识别开始
- **THEN** 第二次识别获得不同的 run ID，第一次识别延迟产生的诊断仍然关联到第一次识别的 ID

#### Scenario: OCR 开始前已经存在识别 ID

- **WHEN** 识别流程调用 OCR recognizer
- **THEN** recognizer 已经持有识别 ID，并可以使用它命名截图和 OCR 诊断文件

### Requirement: 每个诊断文件名都包含识别 ID

当诊断启用时，一次识别写入的每个文件 SHALL 在文件名中包含该识别经过安全处理的 run ID。artifact 前缀 SHALL 继续标识文件类型。同一个 segment 的 LLM request 和 response SHALL 共享 run ID、segment identifier 和 exchange sequence，使两者可以直接匹配。

#### Scenario: 本地诊断文件共享一个 run ID

- **WHEN** 一次识别写入截图、OCR 文本、token 输出和 phrase-analysis 输出
- **THEN** 每个文件名都包含相同的 run ID，同时保留不同的 `frame`、`ocr`、`tokens` 和 `phrase` 前缀

#### Scenario: LLM 请求和响应可以直接匹配

- **WHEN** 一个 sentence segment 产生 LLM 诊断请求和响应
- **THEN** 请求和响应文件名包含相同的 run ID、经过安全处理的 segment identifier 和 exchange sequence

#### Scenario: run ID 对文件名安全

- **WHEN** 系统在目标平台生成诊断文件名
- **THEN** run ID 组件只包含文件名安全字符，不会创建路径分隔符或无效文件名字符

### Requirement: 识别关联信息只保留在本地诊断边界

系统 SHALL 在本地 request 和诊断边界之间传递 run ID，但不得将它加入 provider request payload。run ID SHALL NOT 替换用于语义校验和渲染的现有 sentence segment ID、token ID 或应用管理的 phrase group ID。

#### Scenario: provider payload 不包含 run ID

- **WHEN** 系统从带有 run ID 的 phrase-analysis request 序列化 LLM request body
- **THEN** provider payload 包含该 segment 的语义文本和分析元数据，但不包含 run ID

#### Scenario: segment identity 仍然可用

- **WHEN** 一次识别中分析多个 sentence segment
- **THEN** 每个 request 仍保留自己的 segment ID 用于请求关联，同时所有 request 在本地携带同一个 run ID

### Requirement: 保留现有诊断开关和有界保留策略

本次关联变更 SHALL 保留现有的诊断设置开关、诊断目录、诊断内容、API key 排除行为以及按 artifact 前缀限制保留数量的策略。诊断关闭时，识别 ID SHALL NOT 导致任何诊断文件写入。

#### Scenario: 诊断设置关闭时不写文件

- **WHEN** `DiagEnabled` 不是 `true`
- **THEN** 无论是否存在识别 run ID，都不会写入 OCR、token、phrase 或 LLM 诊断文件

#### Scenario: 关联文件仍遵守保留策略

- **WHEN** 某个 artifact 前缀下的文件数量超过配置的最大值
- **THEN** 系统按照现有保留策略清理该前缀下较旧的关联文件，不删除其他前缀的文件

#### Scenario: LLM 诊断不包含 API key

- **WHEN** 系统使用识别 run ID 记录一次 LLM exchange
- **THEN** request 诊断文件不包含 provider API key，并且 request/response 文件都保留相同的关联组件
