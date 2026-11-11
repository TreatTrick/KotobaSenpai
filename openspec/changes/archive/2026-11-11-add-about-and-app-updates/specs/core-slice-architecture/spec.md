## MODIFIED Requirements

### Requirement: Slice dependencies follow a strict layer order

每个 Core 能力必须（MUST）有明确的层号。它只能依赖层号更小的能力，不能依赖同层或更高层的能力。因此，同一层的能力必须互相独立，不能形成循环依赖。

分层必须（MUST）如下，与现有架构测试保持一致：

| 层号 | 能力 |
| --- | --- |
| 0 | `Geometry`、`Threading`、`Session`、`Japanese`、`Errors`、`Localization`、`Logging`、`Settings`、`Shortcuts` |
| 1 | `Llm`、`TargetWindow`、`Updates` |
| 2 | `Ocr` |
| 3 | `Tokenization` |
| 4 | `Dictionary` |
| 5 | `WordGrouping`、`PhraseAnalysis` |
| 6 | `Diagnostics`、`Overlay` |
| 7 | `Workflow` |

#### Scenario: 依赖更高层时测试失败
- **WHEN** 第 3 层的 `Tokenization` 依赖第 4 层的 `Dictionary`
- **THEN** 架构测试必须失败，并指出违规类型。

#### Scenario: 同层能力不能互相依赖
- **WHEN** 第 5 层的 `PhraseAnalysis` 依赖 `WordGrouping`，或者反过来依赖
- **THEN** 架构测试必须失败。

#### Scenario: 第零层没有对其他能力的依赖
- **WHEN** 检查第 0 层的任何能力
- **THEN** 它不得依赖其他 Core 能力。

#### Scenario: 工作流仍是原有领域流程的入口
- **WHEN** 检查 `Workflow` 之外的任何 Core 能力
- **THEN** 它不得依赖 `Core.Workflow`，避免领域反过来调用上层应用流程。

#### Scenario: 合法的向下依赖不需要修改测试
- **WHEN** 第 6 层的 `Overlay` 增加对第 2 层 `Ocr` 的依赖
- **THEN** 现有架构测试应直接通过，不需要放宽规则。

#### Scenario: 更新能力保持独立
- **WHEN** 检查新增的 `Core.Updates`
- **THEN** 它可以使用第 0 层的错误码，但不能依赖 `Llm`、`TargetWindow`、识别工作流、HTTP、文件系统或 WPF。
