## 为什么

KotobaSenpai 已经包含 OpenAI Chat Completions、OpenAI Responses 和 Anthropic Messages 的协议适配器，但用户无法在应用中配置它们。目前 API Key、endpoint、protocol 和 model 都需要手动编辑 `settings.json`，也没有发现或选择供应商所提供模型的途径。这使云端短语分析对普通用户实际上不可用。

## 变更内容

- 在主设置窗口增加一个当前 LLM 配置区。
- 允许用户启用云端短语分析，并选择 OpenAI Chat Completions、OpenAI Responses 或 Anthropic Messages 协议。
- 允许用户输入 API endpoint 和 API Key；按照用户要求，key 通过现有字符串设置服务存储。
- 增加单个“获取模型”按钮，支持首次获取和再次点击刷新，并提供加载状态、本地化错误状态和可编辑的模型选择器。
- 通过现有扁平设置键持久化所选协议、endpoint、model 和启用状态。
- 后续请求从当前设置解析所选协议，无需重启应用。
- 对短语请求和模型发现请求应用协议专属的认证请求头。
- 为常见的 OpenAI 兼容和 Anthropic 兼容供应商增加模型列表地址回退和响应解析。
- 当供应商没有可用的模型列表地址时，保留手动输入模型的能力。

## 能力

### 新增能力

- `llm-settings`：单一当前云端短语分析连接的用户配置和持久化。
- `llm-model-discovery`：从已配置供应商获取、解析、刷新并选择模型。

### 修改能力

- `llm-protocol-abstraction`：协议选择必须读取当前配置；每个协议必须为分析请求和模型列表请求提供自己的认证要求。

## 影响范围

- `src/KotobaSenpai.App/MainWindow.xaml` 及相关 ViewModel/本地化资源增加 LLM 配置 UI。
- `src/KotobaSenpai.App/App.xaml.cs` 修改协议解析和模型发现的组合根注册。
- `src/KotobaSenpai.Platform.Windows/Llm` 增加模型目录适配器、协议注册表、endpoint 候选处理和协议专属请求认证。
- 现有 `LlmSettingsKeys` 和 `ISettingsService` 继续作为持久化边界；不引入数据库或多供应商 Profile 存储。
- 单元测试和集成风格测试需要覆盖设置绑定、模型列表解析、endpoint 回退、认证请求头、取消以及本地化失败状态。
