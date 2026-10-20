## 1. Core 契约和配置值

- [x] 1.1 在面向 Core 的配置契约中定义支持的 LLM 协议标识和显示元数据；缺失或未知值继续默认使用 OpenAI Chat Completions。
- [x] 1.2 定义 Core 模型目录端口及其结果/错误类型，供应用 ViewModel 使用，且不引用 WPF 或 Windows 实现。
- [x] 1.3 将现有 LLM 设置键移动到或暴露在依赖方向安全的 Core 配置位置，同时保持持久化键名不变。

## 2. 运行时协议解析和认证

- [x] 2.1 增加协议注册表，根据当前 `LlmProtocol` 设置解析三个协议实现，并回退到 OpenAI Chat Completions。
- [x] 2.2 扩展协议边界，提供协议专属的请求认证元数据，并为 OpenAI Chat Completions 和 OpenAI Responses 请求应用 Bearer 请求头。
- [x] 2.3 对 Anthropic Messages 请求应用 `x-api-key` 和 `anthropic-version: 2023-06-01`，不使用 Bearer 认证。
- [x] 2.4 更新 `LlmPhraseAnalyzer`，在请求开始时快照当前 endpoint、key、model 和 protocol，并在不重启应用的情况下解析协议变更。
- [x] 2.5 增加共享的 endpoint 拼接逻辑，避免带版本的 Base URL 产生重复 `/v1` 段，同时保持现有协议路径兼容。

## 3. 模型目录适配器

- [x] 3.1 实现 Windows 模型目录适配器，支持可取消 HTTP 请求、有界超时和脱敏诊断细节。
- [x] 3.2 实现按顺序生成模型列表 URL 候选地址的逻辑，覆盖带版本的 Base URL、普通 Base URL 和已知兼容后缀，并去除重复地址。
- [x] 3.3 对模型列表请求应用所选协议的认证策略，并在候选地址返回 HTTP 404/405 后继续尝试。
- [x] 3.4 解析兼容 OpenAI/Anthropic 的 `data[].id` 响应，以及回退的 `models[].slug`/`models[].id` 响应；对 ID 去除空白、去重并排序。
- [x] 3.5 将缺少凭据、认证失败、超时、不支持的响应格式、空目录、取消和传输错误映射为稳定的目录失败分类。

## 4. 应用 ViewModel 和持久化行为

- [x] 4.1 增加 `LlmSettingsViewModel`，包含可观察的 enabled、protocol、endpoint、API key、model、模型列表、加载状态和本地化状态属性。
- [x] 4.2 将现有五个 LLM 设置键加载到 ViewModel，并通过 `ISettingsService` 立即持久化编辑，同时保留无关设置。
- [x] 4.3 增加统一的获取模型命令，支持首次获取和后续刷新，在本地校验 endpoint/key，向目录端口传递取消 token，忽略过期结果，并在失败时不覆盖当前 model。
- [x] 4.4 保持 model 属性可编辑，即使发现不可用，也要持久化用户手动输入的模型 ID。
- [x] 4.5 确保禁用或不完整的配置继续保留现有本地短语分析回退行为。

## 5. WPF 设置 UI 和本地化

- [x] 5.1 在 `MainWindow.xaml` 增加 LLM 配置卡片，包括启用开关、协议 ComboBox、Base URL TextBox、密码型 API Key 输入框、可编辑模型 ComboBox、单个支持再次点击刷新列表的“获取模型”按钮和加载状态。
- [x] 5.2 增加 API Key 显示/隐藏行为，且不得将 key 放入状态文本或诊断输出。
- [x] 5.3 通过组合根将卡片绑定到 `LlmSettingsViewModel`，不在 App ViewModel 中引入 Platform.Windows 依赖。
- [x] 5.4 为标签、协议名称、提示、加载、成功、空结果、认证、endpoint、超时、不支持目录和通用失败状态增加中性语言及 `zh-CN` 本地化键。
- [x] 5.5 在 `App.xaml.cs` 注册模型目录、协议注册表和 ViewModel，并保持现有单一当前配置行为。

## 6. 测试和验证

- [x] 6.1 增加协议注册表测试，覆盖默认值、未知值和运行时协议选择。
- [x] 6.2 增加请求测试，断言三个协议的路径、endpoint 拼接、请求体和认证请求头。
- [x] 6.3 使用桩 HTTP handler 增加模型目录测试，覆盖 URL 候选、404/405 回退、响应格式解析、规范化、超时/取消和失败分类。
- [x] 6.4 增加 ViewModel 测试，覆盖设置往返、校验消息、成功获取和选择、刷新、过期请求取消、手动输入模型和失败时保留原值。
- [x] 6.5 扩展本地化和架构测试，覆盖新增资源及 Core 端口的依赖方向。
- [x] 6.6 运行 `dotnet test KotobaSenpai.slnx`，并在实现交接前验证已完成的 OpenSpec change。
