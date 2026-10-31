## MODIFIED Requirements

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
