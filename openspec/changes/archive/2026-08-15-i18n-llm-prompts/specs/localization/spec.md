# localization Delta

## ADDED Requirements

### Requirement: 本地化器被非 View 消费方使用
`IStringLocalizer` 端口 SHALL 可供 ViewModel 之外的消费方使用，包括 Platform 层服务（如 LLM prompt 构建器），以便在不将资源文件放入 Core 或 Platform 的前提下，按当前激活文化解析长文本本地化资源（例如 system prompt 文本）。解析 SHALL 在调用时继续反映当前文化。

#### Scenario: LLM prompt 构建器按键解析
- **WHEN** 当前激活文化为 `en` 时，LLM prompt 构建器以某个 prompt 资源键调用 `IStringLocalizer.Get`
- **THEN** 返回的字符串 MUST 为该 prompt 的英文资源值。

#### Scenario: 构建器在下次请求时反映文化切换
- **WHEN** 两次短语分析请求之间激活文化发生变化
- **THEN** 第二次请求 MUST 针对新文化解析 prompt，因为解析发生在调用时而非构造时。