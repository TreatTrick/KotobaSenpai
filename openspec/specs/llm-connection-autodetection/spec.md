# llm-connection-autodetection Specification

## Purpose

Detect supported provider protocols through bounded real Japanese-analysis validation using explicit configuration snapshots and shared transport implementations.

## Requirements

### Requirement: 共用有界的自动协议检测
系统 SHALL 提供 CLI 与 UI 共用的协议检测用例，默认不要求输入协议，仅使用 `LlmProtocolRegistry` 中已支持的实现。默认自动检测 SHALL 按 `openai-responses`、`anthropic`、`openai-chat` 的优先级尝试可用候选，前序协议不适用或验证失败后才尝试后序协议。完整地址后缀与文档提示 SHALL 仅用于匹配和补齐候选地址，不把后序协议提前。检测 SHALL 使用现有认证、地址候选与请求执行实现，最终依据真实分析响应确认协议，不依据密钥前缀、模型名称或目录成功直接确认。

#### Scenario: 用户只提供地址、Key 和模型
- **WHEN** 调用者未填写协议
- **THEN** 自动检测已有协议并返回经验证的具体协议，不要求用户理解或选择协议

#### Scenario: Chat 和 Responses 共用模型目录
- **WHEN** Bearer 认证的模型目录成功返回
- **THEN** 不仅凭目录结果区分 Chat 与 Responses，仍验证候选协议的实际业务请求

#### Scenario: 文档提示错误
- **WHEN** Agent 提供的协议提示与实际服务能力不一致
- **THEN** 不把提示直接持久化为成功配置，在有界候选范围内验证并报告真实结果

#### Scenario: 三种协议都可用
- **WHEN** Responses、Anthropic 和 Chat Completions 均有可用且能通过验证的候选
- **THEN** 优先选择 Responses，不因文档提示或目录顺序先选择另外两种协议

#### Scenario: 优先协议不可用
- **WHEN** Responses 不适用或验证失败，而 Anthropic 可用
- **THEN** 回退到 Anthropic；只有两者均不可用时才尝试 Chat Completions，已有错误停止规则仍然生效

### Requirement: 使用显式候选执行真实日语分析
检测 SHALL 使用不可变候选快照和固定短日语样例，复用正式识别的提示构造、协议请求、结果解析及语义校验，并要求至少一个有效词条或短语。检测 SHALL 不读取屏幕、不依赖 OCR 或词典安装，也不临时写入或启用候选设置。`llm detect` 成功 SHALL 只返回检测结果，正式识别继续使用原配置。

#### Scenario: 普通 HTTP 成功但业务格式不兼容
- **WHEN** 返回 HTTP 200，但内容为 HTML、非 JSON、错误结构或没有有效分析结果
- **THEN** 不报告验证成功，不应用配置

#### Scenario: 检测期间原配置正在使用
- **WHEN** UI 内部检测或 CLI 只读检测与正式识别同时运行
- **THEN** 验证使用候选快照，正式请求使用原配置快照，二者不通过临时修改设置相互影响；CLI 写入操作则先正常退出 GUI，再独占验证与提交

#### Scenario: 仅检测不保存
- **WHEN** `llm detect` 验证成功
- **THEN** 返回实际可用的具体协议、地址和模型，现有四个 LLM 设置键保持原样

### Requirement: 明确处理模型和资料不足
检测 SHALL 区分缺少 Key、缺少 API 地址、缺少模型和模型目录不可用。缺少必填资料时 SHALL 在业务验证请求前返回待补字段；CLI 写入流程 SHALL 在关闭 GUI 前完成此检查。模型发现 SHALL 不要求模型参数，并向 Agent 或 UI 返回服务提供的模型列表。检测与配置 SHALL 使用用户指定或 Agent 选定的模型，目录不可用但已明确提供模型时 SHALL 允许通过实际业务请求验证。

#### Scenario: 缺少或未替换的 Key
- **WHEN** Key 缺失、为空白或仍是工具文档明确定义的占位值
- **THEN** 返回 `missing_api_key` 和 `input_required`，不发供应商请求、不删除原 Key

#### Scenario: 只有文档链接
- **WHEN** 请求只有 `websiteUrl` 而没有实际 API 地址
- **THEN** 返回待补 `endpoint`，不把网页地址当作 API 地址或向文档网站发送 Key

#### Scenario: 目录不支持但模型已知
- **WHEN** 模型目录返回 404/405，且用户或 Agent 已明确提供模型
- **THEN** 检测继续尝试实际分析，不仅因缺少目录而失败

#### Scenario: 检测或配置没有提供模型
- **WHEN** 输入没有 `model` 或值为空白
- **THEN** 返回 `missing_model` 和 `input_required`，由 Agent 选定或 UI 用户选择后继续，不自动采用目录首项，不执行分析验证或关闭 GUI

### Requirement: Agent 自主选模与工具验证职责分离
用户未指定模型时，提示词 SHALL 引导外部 Agent 获取当前服务实际返回的模型列表，自行判断并选择便宜、响应快且适合日语结构化分析的文本模型，再将选定模型交给工具验证。用户明确指定模型时 SHALL 优先采用其指定值。软件 SHALL 负责模型发现、协议检测及选定模型的真实分析验证，不实现费用预测、价格速度评分或自动模型排名，不要求提交价格、速度或选择证据字段。

模型目录没有价格或速度信息 SHALL 不阻止选定模型验证。所选模型无法完成任务时，工具 SHALL 返回具体失败，由 Agent 判断是否选择其他模型再次验证，不在软件内计算替代模型排名。UI 内部检测 SHALL 验证用户填写或从目录选择的模型，复用相同请求与校验能力。

#### Scenario: 用户没有模型偏好
- **WHEN** 模型目录返回多个候选，且用户未指定模型
- **THEN** 提示词要求 Agent 自行选择便宜、快速且适合任务的模型，工具接收选定模型后执行验证，不要求 Agent 提交评分资料

#### Scenario: 目录没有价格和速度字段
- **WHEN** Agent 已选定模型，但目录只包含模型标识等基本信息
- **THEN** 工具正常执行真实分析验证，不返回缺少价格或速度依据的输入错误

#### Scenario: 验证成功
- **WHEN** Agent 选定的模型通过真实日语分析验证
- **THEN** 返回所选模型和验证结果，不为了推测更便宜或更快而继续遍历其他模型

#### Scenario: 选定模型不适合任务
- **WHEN** 所选模型无法返回有效分析结果，且没有候选协议能完成验证
- **THEN** 返回失败供 Agent 判断下一步，不自动换成软件排名中的其他模型，不修改当前配置

#### Scenario: 用户明确指定模型
- **WHEN** 用户已明确要求使用某个模型
- **THEN** 提示词要求优先使用该模型，工具验证传入值，不静默替换；目录不可用时仍保留显式模型验证路径

### Requirement: 检测执行与错误分类
检测 SHALL 沿用共用请求执行层的现有超时与取消机制，按既定协议优先级遍历有限的地址候选，相同协议、方法、地址和模型的请求 SHALL 去重。所选模型的候选验证成功或操作被取消时 SHALL 立即停止。404/405、候选协议不兼容或响应格式不兼容 SHALL 允许继续下一候选。401/403 SHALL 记录该候选失败并允许其他未验证的协议、地址或认证组合继续，不能仅因共享认证方式而排除其他接口。429、5xx、网络失败和超时 SHALL 停止本次操作并返回具体分类，不能直接宣称协议不支持。是否换模型或再次调用工具 SHALL 由 Agent 根据结果判断，不引入费用预测、模型排名或额外的固定总时限、总请求次数探测预算。

#### Scenario: 认证方式不同
- **WHEN** 第一种认证方式返回 401/403，而另一种已注册协议使用不同认证方式
- **THEN** 允许验证另一尚未尝试的认证方式，不将前一次失败直接当作所有协议均不可用

#### Scenario: 相同认证方式的不同接口权限
- **WHEN** Chat 接口返回 401/403，但同用 Bearer 认证的 Responses 接口仍是尚未验证的候选
- **THEN** 继续验证 Responses，不重复原请求，也不提前宣称 Key 无效

#### Scenario: 服务限流或暂时故障
- **WHEN** 收到 429、5xx 或网络失败
- **THEN** 返回对应失败与下一步建议，不无限切换协议或重试

#### Scenario: 请求超时
- **WHEN** 共用 HTTP 执行层报告请求超时
- **THEN** 停止本次操作，返回超时及下一步建议，不把超时直接当作协议不支持，也不自动换模型继续请求

#### Scenario: 请求被取消
- **WHEN** 用户或 CLI 取消当前检测
- **THEN** 传播取消并停止后续请求，不提交配置

### Requirement: 地址边界与最终配置
检测 SHALL 复用现有地址候选规则，保留自定义前缀和查询参数，并只向用户提供或从其文档确认的 API 来源发出带凭据请求，不携带凭据跨来源重定向。检测成功 SHALL 返回实际请求成功的地址及具体协议，配置提交 SHALL 不保存 `auto` 作为协议值。未支持的原生协议 SHALL 返回明确的不支持结果，不能通过写入未知协议字符串假装支持。

#### Scenario: 供应商使用自定义地址前缀
- **WHEN** API 地址包含自定义前缀、版本路径或完整接口路径
- **THEN** 共用候选生成规则，不重复拼接路径、不丢失前缀，并持久化实际验证成功的地址

#### Scenario: 跨来源跳转
- **WHEN** 带凭据的候选请求跳转到其他来源
- **THEN** 不把 Key 自动转发给新来源，返回可诊断的失败

#### Scenario: 所需协议未实现
- **WHEN** 现有三个协议均无法提供所需请求或认证方式
- **THEN** 明确报告不支持并可建议索取兼容入口，不生成新的协议实现或无效设置值
