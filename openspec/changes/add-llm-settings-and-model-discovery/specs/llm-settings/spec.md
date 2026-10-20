# llm-settings 规范

## ADDED Requirements

### Requirement: 配置当前云端短语分析连接
应用 SHALL 在主设置窗口中暴露一个单一的 LLM 配置区。该区域 SHALL 提供启用开关、包含 OpenAI Chat Completions、OpenAI Responses 和 Anthropic Messages 选项的协议选择器、Base URL 输入框、带显示/隐藏控件的 API Key 密码输入框，以及可编辑的模型选择器。

#### Scenario: 显示 LLM 配置控件
- **WHEN** 主设置窗口显示时
- **THEN** 用户可以看到云端短语分析开关、协议选择器、Base URL 字段、API Key 字段、模型字段，以及单个支持首次获取和再次点击刷新列表的“获取模型”按钮

#### Scenario: 隐藏和显示 API Key
- **WHEN** 用户激活 API Key 的显示/隐藏控件
- **THEN** 输入框在掩码文本和明文之间切换，且不改变已存储的值

### Requirement: 持久化当前 LLM 配置
应用 SHALL 通过现有的字符串键设置服务，使用 `PhraseGroupsEnabled`、`LlmProtocol`、`LlmEndpoint`、`LlmApiKey` 和 `LlmModel` 持久化当前 LLM 配置。每个编辑后的值 SHALL 立即写入，修改某个键 SHALL 保留无关设置。

#### Scenario: 保存完整配置
- **WHEN** 用户启用云端分析并保存 protocol、endpoint、API key 和 model 值
- **THEN** 后续设置读取会为五个 LLM 键返回相同的值

#### Scenario: 重启后恢复配置
- **WHEN** 完整配置保存后应用重启
- **THEN** 设置控件显示已持久化的启用状态、protocol、endpoint、掩码后的 API key 和 model

#### Scenario: 保留无关设置
- **WHEN** 在语言、主题或识别设置已保存的情况下修改一个 LLM 字段
- **THEN** 这些无关设置保持不变

### Requirement: 在云端分析前校验配置
应用 SHALL 在云端短语分析被禁用，或请求所需的任一值缺失时，将云端分析视为不可用。UI SHALL 针对缺少 endpoint 或 API key 显示本地化校验消息，并 SHALL 保留本地识别结果。

#### Scenario: 禁用云端分析
- **WHEN** 云端短语分析处于禁用状态
- **THEN** 识别不会发起 LLM 请求，本地单词、读音和下划线仍可使用

#### Scenario: 缺少 API Key
- **WHEN** 云端分析已启用但 API Key 为空
- **THEN** UI 报告需要 API Key，识别返回现有的无 key 回退结果

#### Scenario: 缺少 endpoint
- **WHEN** 云端分析已启用但 Base URL 为空
- **THEN** UI 报告需要 API endpoint，识别不会发送网络请求

### Requirement: 无需重启即可应用设置
每个新的短语分析请求或模型发现请求开始时 SHALL 读取或解析当前的 protocol、endpoint、API key 和 model。修改设置 SHALL 在不重启应用的情况下影响后续请求。

#### Scenario: 为下一次请求修改 protocol
- **WHEN** 用户将 protocol 从 OpenAI Chat Completions 改为 Anthropic Messages，并开始新的识别
- **THEN** 新请求使用 Anthropic Messages 的路径、封装和认证策略

#### Scenario: 快照正在执行的请求
- **WHEN** 请求正在运行时设置发生变化
- **THEN** 正在执行的请求继续使用开始时的配置，后续请求使用新配置

### Requirement: 本地化 LLM 配置反馈
所有面向用户的 LLM 配置、模型获取加载、成功、空结果、认证、endpoint、超时和不支持目录消息 SHALL 可通过现有本地化资源提供。

#### Scenario: 显示本地化的获取反馈
- **WHEN** 模型发现正在加载、成功、没有返回模型，或以分类错误失败
- **THEN** UI 按当前应用文化显示对应消息
