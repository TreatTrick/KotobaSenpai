## 1. 本地化 prompt 资源

- [x] 1.1 在 `src/KotobaSenpai.App/Resources/Strings.resx`（英文/中性）新增 prompt 资源键：`Llm.PhraseSystemPrompt`（英文文本，指示 LLM 以英文书写 `meaning`/`grammar`）、`Llm.PhraseUserInstruction`、`Llm.SegmentLabel`、`Llm.TokenTableLabel`、`Llm.LocalSpansLabel`。
- [x] 1.2 在 `src/KotobaSenpai.App/Resources/Strings.zh-CN.resx` 新增相同键（简体中文值，system prompt 指示 `meaning`/`grammar` 以简体中文输出）。

## 2. 本地化 prompt 构建器

- [x] 2.1 向 `PhrasePromptBuilder`（构造函数）注入 `IStringLocalizer`，并将硬编码的中文 `SystemPrompt`/`UserInstruction` 常量与文内标签替换为在 `Build()` 内部针对激活文化解析的 `_localizer.Get(...)` 调用。
- [x] 2.2 确保 `Build()` 在调用时解析所有本地化字符串，使运行时文化切换在下次请求时被感知。

## 3. 将字段重命名为语言中性名

- [x] 3.1 在 `PhraseGroupSchema.cs` 中将 `meaningZh`/`grammarZh` 重命名为 `meaning`/`grammar`（schema 属性 + required 数组）。
- [x] 3.2 在 `PhraseResponseParser.ParseGroups` 与 `ParsedPhraseGroup` record 中重命名。
- [x] 3.3 在 `PhraseGroup` 与 `PhraseAnalysisRun` 模型中重命名。
- [x] 3.4 更新 `PhraseGroupValidator`、`PhraseGeometryMapper` 与 `WordOverlayApplicationService` 中的用法。

## 4. DI 接线

- [x] 4.1 更新 `App.xaml.cs`，使 DI 注册的 `PhrasePromptBuilder` 接收 `IStringLocalizer`（已作为单例注册）。

## 5. 验证

- [x] 5.1 构建解决方案；确认重命名与新依赖无编译错误。
- [x] 5.2 运行现有测试，并新增/调整任何断言 prompt 文本或 `meaningZh`/`grammarZh` 字段名的测试，改为断言本地化内容与 `meaning`/`grammar`。