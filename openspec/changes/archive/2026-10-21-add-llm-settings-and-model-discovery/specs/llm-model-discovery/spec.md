# llm-model-discovery 规范

## ADDED Requirements

### Requirement: 从已配置供应商获取模型
系统 SHALL 提供可取消的模型发现操作，接收当前 Base URL、API Key 和 protocol，并返回模型标识及可选的所有者元数据。该操作 SHALL 使用有界请求超时，且不得记录或暴露 API Key。

#### Scenario: 获取 OpenAI 兼容目录
- **WHEN** 针对一个提供 `GET /v1/models` 的供应商请求模型发现，且响应成功并包含 `data[].id`
- **THEN** 操作返回非空模型 ID 以及可用的 `data[].owned_by` 值

#### Scenario: 获取另一种目录格式
- **WHEN** 成功的供应商响应包含带 `slug` 或 `id` 值的 `models[]` 条目，且没有可用的 `data` 数组
- **THEN** 操作优先返回非空的 `slug` 值；当 `slug` 缺失时回退到 `id` 值

#### Scenario: 取消模型发现
- **WHEN** 调用方取消模型发现 token
- **THEN** 操作及时停止并报告取消，且不修改当前选中的模型

### Requirement: 推导并尝试模型列表 endpoint 候选
系统 SHALL 根据配置的 Base URL 推导有序的模型列表 URL 候选。由供应商描述符配置的显式模型列表 URL（如果存在）SHALL 最先尝试。以 `/v1` 或 `/v4` 等数字版本段结尾的 Base URL SHALL 使用 `{base}/models`；普通 Base URL SHALL 使用 `{base}/v1/models`；已知兼容后缀 SHALL 额外产生根路径 `/v1/models` 或 `/models` 回退地址。重复候选 SHALL 被移除。

#### Scenario: 使用带版本的 Base URL
- **WHEN** Base URL 为 `https://provider.example/api/v4`
- **THEN** 第一个推导出的候选地址为 `https://provider.example/api/v4/models`

#### Scenario: 使用普通 Base URL
- **WHEN** Base URL 为 `https://provider.example`
- **THEN** 推导出的候选地址为 `https://provider.example/v1/models`

#### Scenario: 为回退地址移除兼容后缀
- **WHEN** Base URL 为 `https://provider.example/anthropic`
- **THEN** 候选地址包含子路径候选以及根路径 `https://provider.example/v1/models` 回退地址

#### Scenario: 在候选地址不可用后继续
- **WHEN** 某个候选地址返回 HTTP 404 或 HTTP 405
- **THEN** 发现流程在报告找不到目录 endpoint 前，继续尝试下一个唯一候选地址

### Requirement: 使用协议专属的模型目录认证
模型发现 SHALL 使用与短语分析相同的协议专属认证策略。OpenAI Chat Completions 和 OpenAI Responses SHALL 发送 `Authorization: Bearer <key>`。Anthropic Messages SHALL 发送 `x-api-key: <key>` 和 `anthropic-version: 2023-06-01`，且不发送 Bearer 请求头。

#### Scenario: 认证 OpenAI 目录请求
- **WHEN** 所选 protocol 是 OpenAI Chat Completions 或 OpenAI Responses
- **THEN** 目录请求包含 Bearer authorization 请求头

#### Scenario: 认证 Anthropic 目录请求
- **WHEN** 所选 protocol 是 Anthropic Messages
- **THEN** 目录请求包含 `x-api-key` 和 `anthropic-version`，且不包含 `Authorization`

### Requirement: 解析并规范化模型结果
模型发现操作 SHALL 去除模型 ID 的首尾空白，丢弃空 ID，去重标识符，并按确定性的 ordinal 顺序返回结果。格式错误的可选 owner 字段 SHALL 不使原本有效的 ID 失效。

#### Scenario: 规范化重复模型条目
- **WHEN** 供应商返回重复 ID、空 ID 以及缺少 owner 的条目
- **THEN** 发现操作按排序顺序只返回每个非空 ID 一次，并在存在有效 owner 值时保留它

#### Scenario: 拒绝不可用的成功响应
- **WHEN** 供应商返回 HTTP 成功，但既没有支持的 `data` 数组，也没有支持的 `models` 数组
- **THEN** 发现操作报告不支持的模型目录错误，并保持当前模型不变

### Requirement: 展示并应用获取到的模型
设置 UI SHALL 提供单个带加载状态的“获取模型”按钮，用于首次获取模型和再次点击刷新列表。成功后 SHALL 让返回的模型在可搜索选择器中可用；选择模型 SHALL 更新并持久化 `LlmModel`。模型字段 SHALL 保持可编辑，以便用户输入发现结果中没有的 ID。

#### Scenario: 获取并选择模型
- **WHEN** 用户输入 endpoint 和 API Key，点击获取操作，并选择一个返回的模型
- **THEN** 选择器显示返回列表，所选模型持久化为 `LlmModel`

#### Scenario: 刷新已有列表
- **WHEN** 用户在上次成功获取后再次点击“获取模型”按钮
- **THEN** UI 显示加载状态，用最新结果替换可用列表，并保留用户手动选择的模型，除非用户选择了其他模型

#### Scenario: 发现失败后保留手动输入
- **WHEN** 模型发现因供应商没有可用目录 endpoint 而失败
- **THEN** UI 显示本地化错误，且仍允许用户手动输入并持久化模型 ID

#### Scenario: 报告分类后的发现失败
- **WHEN** 发现返回认证失败、超时、不支持目录或空结果
- **THEN** UI 显示对应的本地化消息，且不覆盖现有模型
