## ADDED Requirements

### Requirement: 安全容错提取供应商文本 JSON 响应
对于通过文本字符串承载结构化分析结果的协议（OpenAI Responses 的 `output_text` 与 OpenAI Chat Completions 的 `choices[0].message.content`），系统 SHALL 首先执行标准严格 JSON 解析；当标准解析失败时，系统 SHALL 尝试以下受限的安全恢复流程：
1. 去除首尾空白并剥离单一完整 Markdown 代码围栏（` ```json ... ``` ` 或 ` ``` ... ``` `）后重新解析；
2. 若仍失败且首个非空白字符为 `{`，使用 JSON 词法扫描器定位第一个完整平衡的根 JSON 对象 `{ ... }`，仅当该根对象之后至文本末尾的全部剩余字符仅由空白字符和多余的闭合花括号 `}` 构成时，提取该根对象并解析为有效结果，同时记录包含已剥离字符信息的 Warning 日志。

对于根对象本身被截断、字段语法损坏、或根对象之后存在非空白且非 `}` 的多余文本/第二个 JSON 对象的响应，系统 MUST 拒绝恢复并按既有流程判定为 `MalformedJson`，同时继续保留原始响应体的诊断记录。

#### Scenario: 恢复 DeepSeek 尾随多余闭合花括号的响应
- **WHEN** OpenAI Responses 供应商在 `strict: true` 下返回的 `output_text` 为完整合法 JSON 对象末尾多出一个或多个 `}`（例如 `..."translation":"right before one's eyes"}}`）
- **THEN** 系统成功提取并解析第一个完整根 JSON 对象，正常产出该句段的词义、译文与下划线，并输出一条 Warning 日志记录容错恢复

#### Scenario: 恢复被 Markdown 代码围栏包裹的 JSON 响应
- **WHEN** OpenAI Chat Completions 或 OpenAI Responses 返回的文本内容被 ` ```json ... ``` ` 围栏包裹但内部为合法 JSON 对象
- **THEN** 系统剥离围栏并成功解析，不误判为 `MalformedJson`

#### Scenario: 拒绝真正截断或夹带杂质的畸形响应
- **WHEN** 供应商返回的 JSON 文本缺少闭合引号/括号（被截断），或在根对象后附带了额外说明文字或第二个 JSON 对象
- **THEN** 系统不强行截断猜测，仍判定为 `MalformedJson` 并记录诊断文件，同时保留本地振假名可用

## MODIFIED Requirements

### Requirement: Preserve semantic content across protocols
All three protocols SHALL transmit the same canonical semantic content — the segment text, stable token references with UniDic metadata, and locally resolved continuous span summaries（含经 JMdict 回流后的本地有效读音）— and SHALL declare the same structured output fields across `groups`, `words`（包含 `headword`、`pos`、`meaning`、`grammar` 以及按需读音纠偏字符串字段 `reading`）, and optional `translation`. The protocol abstraction SHALL add no semantics beyond wire-format translation, resilient JSON root extraction, and authentication metadata handling. 当供应商响应中的某条 `words` 项省略 `reading` 字段时，解析器 SHALL 将其视为未提供读音覆盖（等价于 `""`），以保持对旧格式和宽松兼容端点的向后兼容。

#### Scenario: Same payload and reading override schema across protocols
- **WHEN** the same phrase request is sent through OpenAI Chat Completions, Anthropic Messages, or OpenAI Responses
- **THEN** all three protocols declare the `words[].reading` string field in their prompt example / tool schema / strict JSON schema, and each protocol's extracted payload is validated by the same parser and validator

#### Scenario: Backward compatibility when provider omits reading field
- **WHEN** a provider returns a valid structured response whose `words[]` items contain `headword`/`pos`/`meaning`/`grammar` without `reading`
- **THEN** the parser treats `reading` as unset rather than failing the segment with `MalformedJson`

#### Scenario: Fall back when a provider returns non-JSON
- **WHEN** a provider ignores its structured-output declaration and returns text that cannot be parsed as a valid JSON object even after safe recovery
- **THEN** the analyzer records a malformed-JSON warning and local words/spans remain available

### Requirement: 优先请求配置地址并有条件回退
短语分析 SHALL 按配置快照（协议标识与配置的原始地址）构造有限的候选地址序列（原始配置地址及按协议生成的版本/路径候选，保留自定义前缀和查询参数，使用 URI 相等性去重且不重复追加版本段或完整协议路径）。

在允许地址回退的正式分析请求（`singleEndpoint == false`）中：
- 若内存缓存中尚无 `(protocolId, configuredEndpoint)` 对应的已验证可用地址，系统 SHALL 首先原样 POST 配置的地址，仅在 HTTP 404/405 时尝试下一个唯一候选；一旦某个候选返回 2xx，系统 SHALL 将该成功地址记入内存缓存；
- 后续针对相同 `(protocolId, configuredEndpoint)` 的分析请求 SHALL 将已缓存的成功地址置于候选序列首位直接发起请求，消除重复的 HTTP 404 往返探测；
- 若已缓存的地址之后返回 HTTP 404/405，系统 SHALL 立即移除该缓存条目，并继续顺次尝试其余未尝试的候选地址。

当显式指定单地址探测（`singleEndpoint == true`）时，系统 SHALL 严格仅请求传入地址，不读取也不写入回退地址缓存。其他非 404/405 失败（HTTP 400/401/403/429/5xx、超时、传输错误、取消或响应格式错误）SHALL 立即返回对应失败结果，不触发地址切换。

#### Scenario: 完整地址直接成功
- **WHEN** 用户填写完整接口地址且该地址返回可解析的 HTTP 成功响应
- **THEN** 分析器只向该地址发送一次请求，不追加任何协议路径

#### Scenario: 首次 404 回退成功后后续句段直接命中缓存地址
- **WHEN** 用户配置 `https://api.deepseek.com` 并使用 OpenAI Responses，第 1 个句段请求根路径返回 404 后回退到 `https://api.deepseek.com/v1/responses` 并返回 200
- **THEN** 后续句段请求直接向 `https://api.deepseek.com/v1/responses` 发送 POST，不再重复向根路径发送 404 请求

#### Scenario: 已缓存回退地址失效时自动驱逐并重新回退
- **WHEN** 已缓存的分析地址随后返回 HTTP 404 或 405
- **THEN** 系统清除该缓存项并继续依次尝试其余唯一候选地址，若新候选成功则更新缓存

#### Scenario: 切换协议或修改配置地址不复用旧缓存
- **WHEN** 用户修改了端点地址或切换了协议
- **THEN** 新请求按新的 `(protocolId, configuredEndpoint)` 键重新探测，不复用旧配置缓存的地址

#### Scenario: 全部地址不可用
- **WHEN** 所有候选均返回 HTTP 404/405
- **THEN** 分析器返回 Refused，警告包含最终 HTTP 状态码和已尝试地址数量

#### Scenario: 其他失败不触发地址切换
- **WHEN** 返回 HTTP 400、401、403、429 或 5xx，或发生超时、传输错误、取消或成功响应格式错误
- **THEN** 分析器立即返回对应失败结果，不再尝试其他地址
