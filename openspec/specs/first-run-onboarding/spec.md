# first-run-onboarding Specification

## Purpose

Define the first-use dictionary, model configuration, target selection and recognition flow, with resumable checkpoints and an explicit state machine.

## Requirements

### Requirement: Present a four-step first-use flow before the main interface

应用 SHALL 在未完成首次引导时，于同一 Fluent 应用窗口依次提供准备词典、配置大模型、选择窗口、开始识别四步。引导 SHALL 显示全部步骤名称、当前步骤及完成状态，保留标题栏与关闭操作；常规识别和设置导航 SHALL 在完成交接前不可访问。第三步 SHALL 提供返回第二步的操作，第四步 SHALL 提供返回第三步的操作；第一、第二步 SHALL 不显示或启用上一步操作，第二步 SHALL 不提供主动返回词典步骤的入口。步骤条 SHALL 仅展示进度，键盘操作 SHALL 遵守相同规则。返回 SHALL 不要求满足前进条件，但在途拾取、预览及其清理、完成交接期间 SHALL 禁止跨步骤操作。实际前置条件失效时的自动准备/修复路由 SHALL 不受手动返回限制。

#### Scenario: Launch a new installation
- **WHEN** 没有引导记录、有效词典或已验证模型配置的安装首次启动
- **THEN** 应用显示第一步及完整四步进度，在保存未完成检查点后启动词典准备，不先显示可操作的主界面

#### Scenario: Prevent navigation around an incomplete step
- **WHEN** 当前步骤的完成条件尚未满足，用户尝试下一步、点击未来步骤或使用导航快捷键
- **THEN** 应用保持当前步骤，不提前暴露常规页面，不发起后续步骤的选择或识别副作用

#### Scenario: Go back to edit the model
- **WHEN** 用户在第三步点击上一步，且没有在途选择或预览操作
- **THEN** 应用返回第二步，保留已有配置和仍有效的当前目标，不自动下载、检测或开始识别；后续继续仍检查实际准入条件

#### Scenario: Go back to review the selected window
- **WHEN** 用户在第四步点击上一步，且尚未开始完成交接
- **THEN** 应用返回第三步，保留仍有效的当前目标和预览，不重复拾取或截图；若需要编辑模型，用户再从第三步返回第二步

#### Scenario: Do not navigate from model configuration back to dictionary preparation
- **WHEN** 用户处于第二步且词典仍有效
- **THEN** 应用不显示上一步按钮，步骤条和返回快捷操作均不能主动进入第一步，不重复下载词典

#### Scenario: Go back without a valid target
- **WHEN** 第三步没有有效目标且当前没有在途选择或预览操作，用户点击上一步
- **THEN** 应用允许返回第二步，不因下一步不可用而禁用返回

#### Scenario: Wait for selection cleanup before navigating back
- **WHEN** 第三步正在拾取、获取预览或清理对应捕获操作
- **THEN** 应用暂时禁用上一步，操作结束并完成清理后恢复返回能力，不产生跨页面并发操作

### Requirement: Keep flow coordination independent of page instances

引导流程协调模块 SHALL 只管理步骤、准入、检查点及能力操作顺序，通过词典、模型和目标选择模块提供的就绪结果与明确操作入口协作。协调模块 SHALL 不依赖具体窗口/页面实例、控件名称、识别页命令绑定或依赖注入容器，不自行实现下载校验、协议请求、验证指纹或目标捕获。运行时准备和开始识别 SHALL 由组合根提供明确入口；共享状态 SHALL 由应用管理最终生命周期，页面卸载 SHALL 只清理自身订阅和临时呈现。

#### Scenario: Exercise the flow without creating pages
- **WHEN** 自动测试使用能力替身和运行时准备/开始识别委托推进引导
- **THEN** 可以验证步骤准入、返回、恢复与识别交接，无须创建 WPF 窗口或页面，无须下载真实词典或调用真实 OCR / LLM

#### Scenario: Preserve shared state while replacing a view
- **WHEN** 引导页面卸载并由下一步或常规页面替换
- **THEN** 旧视图仅清理自身订阅和临时呈现，模型配置及有效目标状态继续由同一共享模块持有，不因视图更换重新初始化运行时

### Requirement: Persist and restore onboarding checkpoints

应用 SHALL 仅通过 `ISettingsService` 将 `OnboardingState` 保存为 `dictionary`、`model`、`window` 或 `completed`，保留其他设置。初次引导 SHALL 在发起下载或展示外部配置入口前保存未完成检查点。恢复 SHALL 同时检查实际词典和当前模型验证状态，不能仅根据保存的步骤宣称已经就绪。第三步返回第二步 SHALL 先保存 `model`，第四步返回第三步 SHALL 保持 `window`。第四步 SHALL 不持久化目标句柄、标题、截图或识别任务，重启后按 `window` 恢复。检查点保存失败 SHALL 显示可重试错误，不继续依赖该保存的跳转或操作。

#### Scenario: Resume after an interrupted dictionary download
- **WHEN** 检查点为 `dictionary` 且词典仍未有效安装
- **THEN** 应用返回第一步并提供安装进度或重试，不将临时文件当作已安装词典，不承诺从已下载字节续传

#### Scenario: Resume after the dictionary became ready
- **WHEN** 检查点为 `dictionary` 且实际词典已经就绪
- **THEN** 应用恢复到第二步，不重复下载

#### Scenario: Restore the model checkpoint after external configuration
- **WHEN** 检查点为 `model` 且词典就绪，应用在 CLI 配置后重新打开
- **THEN** 应用停在第二步并显示最终保存的字段及匹配的验证状态，由用户继续，不因模型已验证而执行已有安装迁移或直接进入主界面

#### Scenario: Require a new window after restart
- **WHEN** 用户在第三或第四步关闭应用，随后重新打开且词典与模型仍就绪
- **THEN** 应用恢复第三步并要求重新选择窗口，不复活旧 HWND、截图或识别请求

#### Scenario: Recheck prerequisites before restoring a later step
- **WHEN** 已保存的后续检查点对应的词典失效或当前模型验证不再匹配
- **THEN** 应用回到第一或第二步处理最早失效条件，不显示后续步骤已就绪

#### Scenario: Fail to save a checkpoint
- **WHEN** 前进或返回所需的检查点写入失败
- **THEN** 应用显示本地化存储错误并允许重试，不报告跳转成功，不发起该跳转后的识别或外部配置操作

#### Scenario: Restart after going back to model configuration
- **WHEN** 用户从第三步返回第二步后关闭应用或通过 CLI 配置后重开
- **THEN** 应用根据 `model` 检查点恢复第二步，保留已保存配置和返回语言，不恢复旧目标或误入主界面

### Requirement: Migrate an existing ready installation conservatively

仅当 `OnboardingState` 键不存在、实际词典就绪且当前 LLM 验证指纹匹配时，应用 SHALL 在本地迁移为 `completed` 后直接显示主界面。迁移 SHALL 不执行网络验证或识别。任意合法未完成值 SHALL 优先于兼容迁移；非空非法值 SHALL 按未完成处理，不参与迁移。已有字段 SHALL 保留，未验证配置 SHALL 进入模型步骤完成检测。

#### Scenario: Upgrade a ready installation
- **WHEN** 无引导键的已有安装具备有效词典和匹配的模型验证记录
- **THEN** 应用保存 `completed` 并显示主界面，不要求重新下载、配置或选择窗口，也不自动开始识别

#### Scenario: Upgrade an installation without model verification
- **WHEN** 无引导键的安装具备有效词典和完整模型字段，但没有匹配验证记录
- **THEN** 应用保存未完成检查点并显示第二步，保留字段供检测，不假定模型可用

#### Scenario: Reject malformed onboarding state
- **WHEN** `OnboardingState` 存在但不是受支持的值
- **THEN** 应用按实际就绪条件进入第一或第二步，不仅因为词典和模型就绪就视为完成

### Requirement: Gate progress on dictionary and model readiness

第一步 SHALL 在实际词典验证及必要运行时准备成功后允许继续，下载完成本身 SHALL 不满足条件。第二步 SHALL 提供与现有模型设置一致的 AI 返回语言、Agent 辅助和手动配置能力，不提供跳过连接检测或本地独立模式；只有当前完整连接配置具有匹配的真实分析验证记录且没有在途模型操作时才允许继续。返回语言 SHALL 可以保持默认跟随值，不要求额外填写。返回编辑或异步结果到达后 SHALL 重新评估准入，旧成功标记不能绕过验证。

#### Scenario: The archive is downloaded but installation is unfinished
- **WHEN** 下载已经结束但校验、解压、原子安装或必要运行时初始化尚未成功
- **THEN** 第一阶段继续显示对应准备状态，不启用进入模型步骤的操作

#### Scenario: Fetch models without verifying analysis
- **WHEN** 用户仅成功获取模型列表或填写并保存了连接字段
- **THEN** 第二步不显示已完成，不允许继续选窗

#### Scenario: Change a previously verified connection
- **WHEN** 用户修改协议、地址、Key 或模型
- **THEN** 应用立即清除旧验证成功状态并禁用继续，直到当前配置检测成功；旧配置的晚到结果不能重新启用继续

#### Scenario: Restore a matching verification record
- **WHEN** 第二步读取到与当前完整配置匹配的验证记录
- **THEN** 应用显示成功并允许用户继续，不自动重复供应商分析检查

#### Scenario: Fail to initialize recognition services
- **WHEN** 词典就绪但识别运行时初始化失败
- **THEN** 应用显示可重试的准备错误并保持未完成状态，不进入无响应页面或保存 `completed`

### Requirement: Configure the AI response language within the model step

第二步 SHALL 提供与设置页相同的 AI 返回语言（释义语言）输入、保存、当前生效语言和本地化校验/保存反馈，沿用 `custom-translation-language` 的格式、草稿、默认跟随及 `ExplanationLanguage` 持久化契约。用户 SHALL 能在配置连接之前保存语言；保存 SHALL 不修改界面语言、Agent 提示词语言或连接四项字段，不清除连接验证状态，不主动发起 OCR、LLM 检测或识别。引导与设置页 SHALL 使用同一偏好，后续实际分析使用已保存的生效值。

#### Scenario: Save a response language before configuring a provider
- **WHEN** 用户在引导第二步尚未填写 API Key 时输入合法语言名称并保存
- **THEN** 语言通过现有偏好服务保存并显示生效值，不发送网络请求；模型连接仍需独立检测通过才能继续

#### Scenario: Keep the default response language
- **WHEN** 用户没有设置自定义语言，或清空输入并保存
- **THEN** AI 返回语言沿用当前界面语言，用户不必填写语言即可在连接验证通过后继续

#### Scenario: Preserve the current language while editing a draft
- **WHEN** 用户编辑返回语言但未保存，或保存时格式校验未通过
- **THEN** 既有生效语言保持原值，首次实际分析不采用未保存草稿，界面沿用设置页的反馈规则

#### Scenario: Change only the response language after verification
- **WHEN** 连接已经检测通过，用户仅修改并保存返回语言
- **THEN** 新语言用于后续分析，连接验证仍有效，不要求重复检测服务商，也不自动开始识别

#### Scenario: Restore the saved response language after Agent configuration
- **WHEN** 用户保存返回语言后通过 Agent 配置连接，应用退出并恢复到第二步
- **THEN** 已保存语言与最终连接配置一并恢复，Agent 提示词仍按界面语言生成，随后进入设置页也显示同一语言偏好

### Requirement: Select a usable target without starting recognition

第三步 SHALL 提供现有桌面拾取、窗口名称、目标可用性、静态预览和重新选择入口。继续 SHALL 要求当前目标身份有效且可捕获，并且拾取和预览捕获已结束清理；没有预览图片本身 SHALL 不代表目标不可用。选择、预览和前进到第四步 SHALL 不调用 OCR、LLM 或启动自动识别。

#### Scenario: Cancel the initial window selection
- **WHEN** 没有目标时用户进入拾取并按 Esc
- **THEN** 应用恢复第三步，保持未选择并禁止继续，不退回模型配置或开始识别

#### Scenario: Continue with a failed static preview
- **WHEN** 当前目标仍有效且可捕获，预览获取失败但其捕获会话已清理
- **THEN** 第三步显示失败及重试入口，允许继续到第四步，不将预览失败伪装为识别失败

#### Scenario: Lose the target before starting
- **WHEN** 第三或第四步的目标关闭或 HWND 身份改变
- **THEN** 应用清除失效选择与图片，保持或返回第三步要求重新选择，晚到预览不能恢复失效目标

#### Scenario: Temporarily minimize a selected target
- **WHEN** 第四步的有效目标暂时最小化或隐藏
- **THEN** 应用禁用开始并提示恢复或重新选择；目标恢复可捕获后允许继续，不为此重新获取静态预览

### Requirement: Complete onboarding at the recognition handoff

第四步 SHALL 展示当前模型及目标窗口摘要，并提供返回第三步和明确的开始识别操作。开始时 SHALL 防止重复执行，重新检查词典、当前模型验证、目标身份及可捕获性和捕获空闲状态，确保主界面及运行时已经准备好。只有成功持久化 `completed` 后，应用 SHALL 切换到主界面并通过明确的开始识别入口对同一个目标调用一次既有识别用例；协调模块 SHALL 不依赖识别页的控件或命令绑定，不重新选窗或获取预览。完成交接期间 SHALL 禁止返回和重复开始。完成 SHALL 不依赖识别到文字或云端请求成功。

#### Scenario: Start recognition once
- **WHEN** 所有条件满足且用户点击第四步的开始识别按钮
- **THEN** 应用保存完成状态、进入主界面并对原目标发起一次识别，连续点击不产生重复请求

#### Scenario: Fail to save completion
- **WHEN** 完成状态持久化失败
- **THEN** 应用保持第四步并显示重试，不发起首次识别，不将当前会话报告为已完成引导

#### Scenario: Return no recognized text or a request failure
- **WHEN** 交接后的首次识别未发现文字，或捕获、OCR、云端请求发生运行时失败
- **THEN** 主界面按现有反馈和重试规则处理，完成状态保留，不重新展示首次引导

#### Scenario: Respect the automatic recognition preference
- **WHEN** 用户完成引导并发起首次识别
- **THEN** 后续自动识别只按现有持久化偏好和会话启动规则运行，引导不自行修改开关或提前启动采样

#### Scenario: Restart after completion was saved
- **WHEN** 完成状态保存后进程退出，包括识别请求结果尚未返回的情况
- **THEN** 下次启动直接进入主界面，不自动重放可能已经发送的识别请求

### Requirement: Keep completed users on the normal startup path

`completed` 用户后续启动 SHALL 进入主界面，按既有行为等待用户选择窗口和开始识别。LLM 配置缺失、验证过期或网络故障 SHALL 使用常规设置与错误反馈处理，不重置首次引导。词典丢失或无效时 SHALL 使用独立启动准备/修复状态，修复默认词典后进入主界面，保留完成状态；显式非法目录覆盖 SHALL 按既有覆盖契约报告错误，不静默切换目录。

#### Scenario: Launch after completing onboarding
- **WHEN** 完成状态有效且词典就绪
- **THEN** 应用直接显示主界面，不重新显示四步、不自动恢复历史目标、不自动开始识别

#### Scenario: Repair a missing default dictionary
- **WHEN** 已完成用户的默认词典缺失或损坏
- **THEN** 应用显示可重试的独立词典准备状态，成功后进入主界面，不要求再次配置模型和完成四步

#### Scenario: Fail a provider request after onboarding
- **WHEN** 已完成用户的服务商暂时不可达或配置后来被删除
- **THEN** 应用在常规界面提供错误和设置入口，不清除完成状态或强制重新引导

### Requirement: Preserve localization, theme and accessible operation

引导 SHALL 复用现有 Fluent 资源、主题和本地化端口，所有新增用户文案 SHALL 提供英文和简体中文 `.resx` 资源。步骤、操作、进度和错误 SHALL 可通过键盘及辅助技术识别；切步后 SHALL 将焦点置于当前内容，进行中的状态 SHALL 可被播报。主题或文化变化 SHALL 不重启下载、验证、选窗或识别，关闭窗口 SHALL 取消和释放已有在途操作及临时资源。

引导外壳 SHALL 始终提供可用的界面语言选择器，覆盖全部四步、下载、运行时准备和错误重试状态；选项 SHALL 使用各语言自身名称。选择 SHALL 复用现有界面语言偏好，立即更新文案并持久化，保留当前步骤、已填配置、目标和正在执行的操作；独立保存的 AI 返回语言 SHALL 不受影响。

#### Scenario: Use the guide with a keyboard and screen reader
- **WHEN** 用户通过键盘操作步骤按钮并读取进度或错误
- **THEN** 当前步骤、按钮准入和反馈可被识别，焦点不进入隐藏主界面，禁用按钮不能绕过门禁

#### Scenario: Change theme or culture during preparation
- **WHEN** 应用文化或主题在某一步操作期间改变
- **THEN** 现有视图更新文案和主题，保留正在运行的操作与步骤状态，不重复请求

#### Scenario: Choose a display language at any onboarding step
- **WHEN** 用户在任一步骤、下载、运行时准备或错误状态中通过引导外壳切换界面语言
- **THEN** 选择器始终可见且可用，文案立即更新，步骤与检查点保持不变，不重启操作；后续步骤、主界面及重启沿用已保存的界面语言偏好

### Requirement: Present the approved design with native WPF controls

引导 SHALL 使用已确认原型的留白双栏、大标题、圆点步骤条、紧凑模型表单和横向真实窗口预览，并使用原生 WPF 控件及现有动态主题资源。顶部主题和语言选择 SHALL 始终可用。底部前进、返回及重试 SHALL 独立于内容滚动，在最小 900×620 窗口仍可访问。原型模拟下载、配置、选窗和识别结果 SHALL NOT 进入实际应用。

#### Scenario: Read a long model form in a small window
- **WHEN** 用户在 900×620 窗口查看模型配置、错误或长文案
- **THEN** 内容区可以纵向滚动，主题与语言选择及底部操作仍可访问，协议、密钥显隐、模型获取、真实检测、取消与错误复制继续使用同一配置状态

#### Scenario: Change a step with system animations disabled
- **WHEN** 用户切换引导步骤，且 Windows 关闭客户端动画或启用高对比度
- **THEN** 新步骤立即完整显示，焦点与业务步骤正常更新，不等待动画、不产生额外下载、检测、捕获或识别请求

#### Scenario: Refresh language or theme during an animated flow
- **WHEN** 用户调整界面语言或主题，或进行中的操作报告新状态
- **THEN** 仅更新相应呈现，不重播切步动画、不重建共享配置与目标状态；页面卸载时移除本页动画时钟

#### Scenario: Close before the runtime exists
- **WHEN** 用户在词典下载或首次运行时初始化之前关闭窗口
- **THEN** 应用正常退出并清理已创建资源，不因退出清理而创建尚未就绪的 OCR、分词器或完整识别对象图

### Requirement: Centralize flow in an explicit state machine

引导 SHALL 由独立于视图、ViewModel 和容器的显式状态机维护唯一可写状态，集中定义初始化、前进、返回、恢复、关闭的准入和效果顺序。准备、可操作、准备失败、完成后修复、启动识别和关闭 SHALL 是可区分的阶段。ViewModel SHALL 仅适配命令与本地化呈现；宿主 SHALL 按当前步骤装载一个完整步骤视图，并复用同一个模型配置和目标状态。

#### Scenario: Ignore repeated initialization after a step becomes active
- **WHEN** 引导成功准备并停留在可操作步骤，再次收到初始化请求
- **THEN** 保持当前步骤、检查点和配置，不再次调用准备入口

#### Scenario: Retry a cancelled preparation without reopening the application
- **WHEN** 准备被取消且流程尚未关闭
- **THEN** 进入可重试的准备失败阶段，保留检查点并禁止未就绪的前进；重试按原规则恢复

#### Scenario: Refresh presentation without replacing the active step
- **WHEN** 界面语言、主题或就绪反馈变化，而当前步骤不变
- **THEN** 保持活动步骤视图实例，仅更新文案、样式和操作准入，不实例化隐藏步骤或重播切步动画

#### Scenario: Ignore late preparation after closure
- **WHEN** 流程关闭后异步准备才返回成功或失败
- **THEN** 保持已关闭状态，不切换步骤、不识别，后续准入查询不得读取已释放的能力服务
