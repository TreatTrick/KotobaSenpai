## Why

LLM 短语分析请求被硬编码为简体中文：`PhrasePromptBuilder` 内嵌中文 system prompt、中文用户指令与中文标签，且响应 schema 的字段被字面命名为 `meaningZh`/`grammarZh`。当应用 UI 语言为英文时，prompt 仍以中文发出，LLM 的释义也以中文返回——这与应用自身的 i18n（`localization` spec）不一致。prompt 及其响应应当跟随当前激活的 UI 文化。

## What Changes

- 本地化 LLM 短语分析**请求**：system prompt、用户指令与用户内容标签改为通过现有的 `IStringLocalizer` 端口（Core）解析，从而根据激活文化自动加载 `zh-CN` 或 `en` 资源，取代 `PhrasePromptBuilder` 中的硬编码中文常量。
- 本地化 LLM **响应**：本地化的 system prompt 指示 LLM 以激活语言书写 meaning/grammar 释义字段。
- **BREAKING** 将响应 schema 与模型字段 `meaningZh`/`grammarZh` 重命名为 `meaning`/`grammar`（语言中性），因为字段内容不再必然为中文。此改动波及 JSON schema、`PhraseResponseParser`、`ParsedPhraseGroup`、`PhraseGroup` 以及负责映射/校验的 Core 服务。
- 在 App 层资源文件（`Strings.resx` 中性/en、`Strings.zh-CN.resx`）中新增本地化 prompt 资源。

## Capabilities

### New Capabilities
- `i18n-llm-prompts`: LLM 短语分析 prompt 与响应针对当前激活 UI 文化的本地化。

### Modified Capabilities
- `llm-protocol-abstraction`: 共享的 prompt 构建器现在产出随文化变化的 prompt 文本，且响应 schema 字段名由 `meaningZh`/`grammarZh` 改为 `meaning`/`grammar`。
- `localization`: `IStringLocalizer` 端口现在也被 LLM prompt 构建器（Platform 层）消费（而不仅是 ViewModel），用于解析长文本 prompt 资源。

## 影响

- `src/KotobaSenpai.Platform.Windows/Llm/PhrasePromptBuilder.cs` — 注入 `IStringLocalizer`，按键解析 prompt 字符串。
- `src/KotobaSenpai.Platform.Windows/Llm/PhraseGroupSchema.cs` — 重命名 schema 字段。
- `src/KotobaSenpai.Platform.Windows/Llm/PhraseResponseParser.cs` — 重命名解析字段。
- `src/KotobaSenpai.Core/Models/ParsedPhraseGroup.cs`、`PhraseGroup.cs`、`PhraseAnalysisRun.cs` — 重命名字段。
- `src/KotobaSenpai.Core/Services/PhraseGroupValidator.cs`、`PhraseGeometryMapper.cs`、`WordOverlayApplicationService.cs` — 重命名用法。
- `src/KotobaSenpai.App/App.xaml.cs` — `PhrasePromptBuilder` 为 DI 注册对象；注入 `IStringLocalizer`。
- `src/KotobaSenpai.App/Resources/Strings.resx` + `Strings.zh-CN.resx` — 新增 prompt 资源键。
- 线上契约变更：LLM 接收/返回的 JSON schema 字段名由 `meaningZh`/`grammarZh` 变为 `meaning`/`grammar`。