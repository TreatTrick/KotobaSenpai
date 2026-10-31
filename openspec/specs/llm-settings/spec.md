# llm-settings Specification

## Purpose
Define the LLM configuration controls, persistence, validation, runtime updates, and localized feedback for cloud phrase analysis.

## Requirements

### Requirement: 配置当前云端短语分析连接
应用 SHALL 将云端 AI 分析作为必需流程，在设置页和首次引导第二步复用同一个 LLM 配置区，不提供启用或禁用开关。该区域 SHALL 按现有设置页同时提供 AI 返回语言（释义语言）输入及保存、Agent 辅助配置和手动配置。返回语言 SHALL 沿用 `custom-translation-language` 的独立偏好和保存契约，不与连接字段或界面语言绑定。Agent 区域 SHALL 提供跟随当前应用语言的 AI 配置提示词入口、UI 内部检测进度，并说明外部工具写入配置时应用会正常退出、结束后恢复原先打开的 UI。高级手动配置 SHALL 默认展开并提供依次为 OpenAI Responses、Anthropic Messages、OpenAI Chat Completions 的协议选择器、支持基础地址或完整接口地址的 API 地址输入框、带显示/隐藏控件的 API Key 密码输入框，以及可编辑的模型选择器。UI SHALL 提供仅检查当前配置的操作，使用当前选定协议及正式识别的请求、解析和校验流程，不自动切换协议、不回填或应用连接字段，也不重启 GUI；自动发现协议并保存配置 SHALL 继续由外部 Agent 的配置工具提供。

#### Scenario: 显示 LLM 配置控件
- **WHEN** 用户打开设置页或首次引导第二步
- **THEN** 可以看到 AI 返回语言输入及保存、Agent 配置入口，以及协议、API 地址、API Key、模型和获取模型按钮，不显示云端分析启用开关；AI 配置入口在无需展开高级字段时即可使用

#### Scenario: 隐藏和显示 API Key
- **WHEN** 用户激活 API Key 的显示/隐藏控件
- **THEN** 输入框在掩码文本和明文之间切换，且不改变已存储的值

#### Scenario: 从 UI 检查当前配置
- **WHEN** 用户填写协议、地址、Key 和模型，并使用高级手动配置顶部左侧的“检测配置是否可用”按钮
- **THEN** 仅按当前选定协议执行真实分析检查，不自动改用其他协议；成功或失败均保留四项连接字段，只记录对应验证状态

#### Scenario: 告知外部配置会暂时退出应用
- **WHEN** 用户查看 AI 配置卡片
- **THEN** 按当前语言说明外部写入会中断当前识别并自动恢复原先打开的 UI，只读命令不关闭界面

#### Scenario: Share the current connection across onboarding and settings
- **WHEN** 用户在引导配置模型后进入主界面设置页
- **THEN** 设置页显示同一连接配置、对应验证状态及已保存的返回语言，不另存引导专用配置，不重复获取模型或执行分析检测

### Requirement: 持久化当前 LLM 配置
应用 SHALL 通过现有字符串键设置服务，使用 `LlmProtocol`、`LlmEndpoint`、`LlmApiKey` 和 `LlmModel` 四项保存当前连接配置，不再读取或写入云端分析启用状态。历史 `PhraseGroupsEnabled` 值 SHALL 不影响运行。API Key 继续以现有 JSON 形式保存。高级手动编辑 SHALL 保留即时保存行为；Agent 与自动检测流程 SHALL 先验证候选，再以原子条件批量提交应用完整的当前配置和该配置的验证指纹，检测期间不写入候选。验证指纹 SHALL 不包含明文密钥。协议 SHALL 保存验证得到的具体标识，不保存 `auto`。无关设置 SHALL 保留，历史撤销记录不再读取或执行。

#### Scenario: 保存完整配置
- **WHEN** 用户手动编辑配置，或经验证的 Agent 配置成功提交
- **THEN** 后续读取返回对应完整配置；Agent 提交的四个键一次生效，不暴露半套配置

#### Scenario: 重启后恢复配置
- **WHEN** 完整配置保存后应用重启
- **THEN** 设置控件显示已保存的具体协议、地址、掩码 Key 和模型，无需重新填写密钥；云端分析始终为必需流程

#### Scenario: 保留无关设置
- **WHEN** 修改 LLM 设置或应用 Agent 配置
- **THEN** 已有语言、主题、识别偏好及其他未知字段保持原值或其并发更新后的值

#### Scenario: 检测失败不替换配置
- **WHEN** 候选验证失败、提交前取消或条件提交发生冲突
- **THEN** 原有完整配置保持可用，UI 显示对应结果，不声称已启用新服务

### Requirement: 在云端分析前校验配置
识别流程 SHALL 必须包含云端分析，不提供可禁用的本地独立模式。配置缺少地址、API Key 或模型时 SHALL 返回现有的配置缺失状态并提示补全，不发送无效请求。已具备必要配置时 SHALL 不受任何历史启用值影响而执行分析；请求失败 SHALL 返回分类错误，不伪装为分析成功。

#### Scenario: 历史禁用值不再生效
- **WHEN** 历史设置包含 `PhraseGroupsEnabled=false`，或完全没有此键
- **THEN** 识别仍执行云端分析流程，分析器不因为该键而跳过请求

#### Scenario: 缺少 API Key
- **WHEN** API Key 为空
- **THEN** 返回现有的配置缺失状态，提示补充 API Key，不发送没有凭据的请求，不进入独立本地模式

#### Scenario: 缺少 endpoint
- **WHEN** API 地址为空
- **THEN** 返回现有的配置缺失状态，提示补充地址，不发送无效请求

### Requirement: 无需重启即可应用设置
每个新的短语分析或模型发现请求 SHALL 通过共用配置适配读取一致的配置快照。GUI 内的手动修改 SHALL 在不重启的情况下影响后续请求；手动检测只更新验证状态，不重新保存或回填四项连接字段。外部 CLI 写入 SHALL 使用正常退出并重新打开 GUI 的流程，新进程从最终持久化配置初始化；该路径 SHALL 不要求跨进程热更新、设置变更订阅或文件监听。

#### Scenario: 为下一次请求修改 protocol
- **WHEN** 用户将协议从 OpenAI Chat Completions 改为 Anthropic Messages，并开始新的识别
- **THEN** 新请求使用 Anthropic Messages 的路径、封装和认证策略

#### Scenario: 快照正在执行的请求
- **WHEN** 请求正在运行时设置发生变化
- **THEN** 当前请求继续使用开始时的完整快照，后续请求使用新配置

#### Scenario: UI 内部检测不应用配置
- **WHEN** 设置页检测当前配置成功
- **THEN** 显示成功状态，四项连接字段保持原样，不执行配置应用或回填，不重启 GUI

#### Scenario: CLI 配置后重新打开设置页
- **WHEN** CLI 完成配置并恢复原先打开的 GUI
- **THEN** 新 ViewModel 读取最终保存的协议、地址、掩码 Key 和模型，不等待外部变更通知

#### Scenario: Restore the model configuration inside unfinished onboarding
- **WHEN** CLI 配置结束后重新打开仍处于引导模型检查点的 GUI
- **THEN** 应用在引导第二步读取最终字段和匹配的验证记录，不自动跳到主界面、不重复网络检测，用户仍可查看结果并继续

#### Scenario: Preserve normal startup after onboarding
- **WHEN** 已完成引导的用户经 CLI 配置后恢复 GUI
- **THEN** 应用沿用常规主界面启动路径读取最终配置，不因为此次配置重新打开首次引导

### Requirement: 本地化 LLM 配置反馈
所有面向用户的 LLM 配置、模型获取加载、成功、空结果、认证、endpoint、超时和不支持目录消息 SHALL 可通过现有本地化资源提供。

#### Scenario: 显示本地化的获取反馈
- **WHEN** 模型发现正在加载、成功、没有返回模型，或以分类错误失败
- **THEN** UI 按当前应用文化显示对应消息

### Requirement: 本地化的配置进度和恢复反馈
AI 配置卡片 SHALL 沿用现有 Fluent 视觉资源，按当前应用文化展示 UI 自己发起的资料检查、自动检测、实际分析验证和失败状态。“检测配置是否可用”按钮 SHALL 位于高级手动配置顶部左侧，成功时在右侧显示绿色圆圈勾和“下方配置可以使用”；失败时显示红色三角感叹号、“配置不可用”、本地化原因和可复制的脱敏错误详情，最右侧提供“复制错误信息”按钮。状态、图标和文字 SHALL 支持深浅主题和运行时语言切换。外部 CLI 操作期间 SHALL 不要求向已关闭的 GUI 推送进度，工具结果由 Agent 解释；恢复后的设置页 SHALL 仅在验证指纹与当前四项配置一致时恢复绿色成功状态。只获取到模型列表 SHALL 不展示配置完成。不要求用户填写价格、速度或模型评分。

#### Scenario: 等待用户补 Key
- **WHEN** 共用配置用例返回缺少 API Key
- **THEN** UI 以当前语言显示需要补充密钥，不显示成功或覆盖现有字段

#### Scenario: 运行中切换语言
- **WHEN** UI 内部配置操作期间应用语言改变
- **THEN** 界面按当前阶段和稳定结果码重新本地化，操作本身不被重复执行

#### Scenario: 验证通过后完成
- **WHEN** 实际分析验证成功且完整配置已保存
- **THEN** 在检测按钮右侧显示绿色圆圈勾和“下方配置可以使用”，清除进度文字，高级手动字段保持展开；手动检测不改变连接字段

#### Scenario: 检测等待动画
- **WHEN** 用户点击“检测配置是否可用”且操作尚未结束
- **THEN** 按钮内显示旋转等待环并禁止重复点击，完成、失败或取消后停止并隐藏等待环，恢复原图标和可点击状态

#### Scenario: 复制反馈保持在 AI 配置卡片内
- **WHEN** 用户复制配置提示词成功或失败，或复制入口发现工具缺失
- **THEN** 在 AI 配置卡片内、说明文字下方显示本地化反馈，成功配绿色圆圈勾，失败配红色警告；反馈支持中英文和深浅主题，不显示在卡片外，不覆盖手动检测状态

#### Scenario: 检测反馈保持在检测卡片内
- **WHEN** 手动检测正在执行、取消、失败或用户复制错误详情
- **THEN** 按钮、进度、取消提示、错误详情及复制反馈均在同一检测卡片内显示，不在卡片外追加操作提示文字

#### Scenario: 配置变更后清除旧结果
- **WHEN** 协议、地址、密钥或模型发生变化，包括正在检测时修改字段
- **THEN** 清除旧成功或失败提示、隐藏错误复制按钮，正在运行的检测取消，过期结果不得覆盖当前字段或显示成功

#### Scenario: 检测失败后复制错误
- **WHEN** 手动检测失败
- **THEN** 在按钮右侧显示红色三角感叹号和“配置不可用”，下方展示本地化错误原因及技术详情；复制按钮复制相同详情且不包含密钥

#### Scenario: 重新验证失败
- **WHEN** 对当前已验证的相同配置重新检测失败
- **THEN** 清除该配置的验证指纹，重开界面不显示过时的成功状态；不同候选失败不得清除原配置的有效验证状态

#### Scenario: 尚未选择模型
- **WHEN** UI 内部检测没有提供模型
- **THEN** 按当前语言提示选择或填写模型，不要求填写价格、速度或选择证据；使用外部 Agent 时，由 Agent 判断并选定模型
