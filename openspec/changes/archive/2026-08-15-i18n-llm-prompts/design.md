## Context

`PhrasePromptBuilder`（Platform.Windows）硬编码了中文 system prompt、中文用户指令与中文用户内容标签，并构建出一个共享 prompt，供三种 `ILlmProtocol` 实现共用。响应 schema 与下游模型将散文字段命名为 `meaningZh`/`grammarZh`。应用已具备 `localization` 能力：一个 Core 的 `IStringLocalizer` 端口（按键、针对 `CurrentUICulture` 解析，英文为中性回退），在 App 层通过 `ResourceManagerStringLocalizer` 基于 `Strings.resx`（中性/en）与 `Strings.zh-CN.resx` 实现。`LanguageService` 在启动与运行时切换时设置 `CurrentUICulture`。

约束：`localization` spec 禁止 Core 与 Platform 包含本地化资源文件或文化解析逻辑——资源位于 App，通过 Core 端口解析。

## 目标 / 非目标

**目标：**
- LLM 请求 prompt（system prompt + 用户指令 + 标签）跟随当前激活的 UI 文化。
- LLM 响应散文（meaning/grammar）由 prompt 驱动，以当前激活的 UI 文化书写。
- 令 prompt 在线上契约中保持语言中性（语言中性的字段名）。
- 遵循现有分层：Platform 依赖 Core 的 `IStringLocalizer` 端口；资源留在 App。

**非目标：**
- 不做 LLM 结果面板的本地化 UI（超出字段内容本身，已由现有 localization 处理）。
- 不做响应语言协商（例如 schema 中加 `language` 字段）——由 prompt 控制。
- 不重构协议抽象。

## Decisions

### 1. 通过 Core 的 `IStringLocalizer` 端口解析 prompt 文本
`PhrasePromptBuilder` 增加对 `IStringLocalizer`（Core）的构造依赖。硬编码的中文常量改为资源键，在 `Build()` 内部解析：`Llm.PhraseSystemPrompt`、`Llm.PhraseUserInstruction`，以及诸如 `Llm.SegmentLabel`、`Llm.TokenTableLabel`、`Llm.LocalSpansLabel` 的标签键。

- **为什么**：满足 localization 分层规则（Platform 不放资源文件），复用已 DI 注册的端口，并免费获得英文中性回退。
- **否决的备选方案**：在 Platform 内嵌两套常量并按文化选择——违反 "Platform SHALL NOT contain localization resource files" 规则，且重复 `LanguageService` 已负责的文化逻辑。

### 2. 在 `Build()` 调用时解析，而非构造时
因为 `IStringLocalizer.Get` 在每次调用时读取 `CurrentUICulture`，在 `Build()` 内部解析意味着运行时语言切换会在下一次短语分析请求时被感知，无需订阅 `CultureChanged`。构建器为单例；分析器每次请求调用 `Build()`。

- **为什么**：零额外状态或事件接线；请求路径天然是按请求的。
- **否决的备选方案**：订阅 `CultureChanged` 并缓存文化——没有必要，因为请求之间没有任何 prompt 被预计算。

### 3. 将 `meaningZh`/`grammarZh` 重命名为 `meaning`/`grammar`
响应 schema、`PhraseResponseParser`、`ParsedPhraseGroup`、`PhraseGroup`、`PhraseAnalysisRun` 以及 Core 的映射/校验服务将两个散文字段重命名为语言中性的名字。纯机械重命名，无逻辑变更。

- **为什么**：一旦响应可能为英文，字面名为 `meaningZh` 的字段会误导其内容。内容语言由 prompt 控制，字段名应当中性。
- **否决的备选方案**：保留 `meaningZh`/`grammarZh` 并让 prompt 决定内容——功能上可行但语义错误，对维护者造成困惑。

### 4. 通过本地化 system prompt 指定响应语言
`zh-CN` 的 system prompt 资源指示 LLM 以简体中文书写 `meaning`/`grammar`；`en` 资源指示英文。schema 中不新增语言字段。

- **为什么**：prompt 是输出语言的唯一来源；新增 schema 字段会传输冗余状态并扩大线上契约。

### 5. 资源放置
在 `Strings.resx`（英文/中性）与 `Strings.zh-CN.resx`（简体中文）中新增 prompt 键。中性 `Strings.resx` 即英文回退，符合现有 localization 契约。

## 风险 / 权衡

- [resx 中的长资源字符串] → resx 支持多行值；system prompt 是一个字符串，对 `GetString` 无碍。
- [字段重命名是线上契约变更] → 对任何持久化/记录的 JSON 是破坏性的；无外部消费方（内部进行分析），影响局限于 proposal 中列出的重命名点。
- [prompt 语言与持久化数据语言不一致] → 释义是短时展示文本；不以需要 locale 标记的方式跨重启持久化。超出范围。

## 开放问题

- 无阻塞项。重命名点已在 proposal 中枚举；具体资源键名为惯例命名。