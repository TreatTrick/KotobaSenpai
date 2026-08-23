## Why

`KotobaSenpai.Core` 按技术类型分包：69 个文件中有 59 个挤在 `Models`(31) / `Services`(14) / `Contracts`(14) 三个平铺命名空间里。一个业务能力的值对象、服务和端口被撕成三份，修改任何一处都要跨三个文件夹来回跳，且文件按字母排序，看不出哪些属于同一能力。

这种结构还掩盖了职责错位：把现有文件按业务能力试排后，暴露出 4 组反向依赖（`Tokenization`↔`Dictionary`、`PhraseAnalysis`↔`Overlay`、共享层反向依赖下游、`Tokenization`→词组合）和 1 处生产代码零调用的死方法。在平铺命名空间下这些问题不可见，也没有任何自动化约束能发现它们。

跨项目的依赖方向（Core / Platform.Windows / App 六边形分层）本身是健康的，由 12 条 NetArchTest 规则守着，本次不动。问题只在 Core 层内部。

## What Changes

- 将 `KotobaSenpai.Core` 从 3 个技术命名空间重组为 11 个能力切片：`Geometry`、`Japanese`、`Errors`、`TargetWindow`、`Ocr`、`Tokenization`、`Dictionary`、`WordGrouping`、`PhraseAnalysis`、`Diagnostics`、`Overlay`、`Workflow`。每个切片内同时容纳该能力的值对象、领域服务与端口接口。
- 移除 `Models/`、`Services/`、`Contracts/` 三个文件夹。端口接口迁至其消费方所在切片，`Contracts/` 解散。
- 不引入 `Shared/` 或 `Common/` 目录。共享内容按真实身份拆为 `Geometry`（坐标）、`Japanese`（假名/音高/UniDic 常量）、`Errors`（领域异常与错误码）三个零出度切片。
- 修正 7 处职责错位：`SentenceTokenBuilder`、`LocalSpanSummary`、`PhraseGeometryMapper` 归入 `PhraseAnalysis`；`IDiagnosticReporter` 独立为 `Diagnostics` 切片；`ErrorCodes` 与 `IUserFacingException` 从 `Localization/` 迁入 `Errors/`；`WordMeaningView.FromWord(GroupedWord)` 收入 `WordOverlaySession`。
- 将领域唯一入口 `WordOverlayApplicationService` 独立为顶层 `Workflow` 切片，与聚合根 `WordOverlaySession` 所在的 `Overlay` 切片分离。
- 删除 `CoordinateMapper.ToScreen(OcrWord, …)` 重载——生产代码零调用，仅由两个测试撑活；连同 `WindowWordOverlayTests` 与 `CoordinateTransformTests` 中对应的两个测试一并移除。
- `KotobaSenpai.Core.Tests` 按同名切片建立镜像文件夹结构。
- 新增架构约束：`Architecture.Tests` 增加一条层序 `[Theory]`，断言每个切片只能依赖层号严格更小的切片（同层互禁，从而同时保证无环、无上游依赖、独立切片互不侵入）；仓库根新增 `.editorconfig` 将 `IDE0130`（命名空间与文件夹不匹配）设为 `error`。
- 新增 `.git-blame-ignore-revs`，记录纯机械搬迁提交以保住 `git blame`。
- 清理归档后残留的空目录 `openspec/changes/follow-target-window/`。

本次为纯结构重构，不改变任何运行时行为、用户可见文案、配置键或对外契约。

## Capabilities

### New Capabilities

- `core-slice-architecture`: Core 层的模块边界约定——按业务能力垂直切片、端口与消费方同片、切片层序与同层互禁规则，以及命名空间必须与文件夹一致的编译期约束。这些要求此前只存在于口头约定，本次成为可执行规范。

### Modified Capabilities

无。本次不改变任何现有能力的需求级行为。`localization`、`logging`、`settings` 三个 spec 中关于「端口在 `KotobaSenpai.Core`、实现在 `KotobaSenpai.App`」的架构要求以程序集为粒度，`ErrorCodes` 与 `IUserFacingException` 的迁移仍留在 `KotobaSenpai.Core` 程序集内，不触发需求变更。

## Impact

- **`src/KotobaSenpai.Core`**：全部 69 个文件更换文件夹与命名空间；`Models/`、`Services/`、`Contracts/` 移除；7 处职责归位；删除 1 个死方法。
- **`src/KotobaSenpai.Platform.Windows`、`src/KotobaSenpai.App`**：仅 `using` 语句更新，无逻辑变更。文件夹结构不动——Platform 已是垂直切片且按技术命名（`Capture/`、`Llm/`），对适配器层是正确的；App 是组合根与视图层，按技术关注点分包本就恰当。
- **`tests/`**：`Core.Tests` 建立镜像切片结构；三个测试项目更新 `using`；`Architecture.Tests` 新增层序规则；移除 2 个仅覆盖死代码的测试。
- **仓库根**：新增 `.editorconfig`、`.git-blame-ignore-revs`。
- **验收基线**（已实测）：`dotnet build` 0 警告 0 错误；277 个测试全绿，约 4 秒跑完。终态须保持 0 警告 0 错误、275 个测试全绿（差额为主动删除的 2 个死代码测试）。
- **不涉及**：领域事件、仓储抽象、CQRS、独立程序集拆分——单聚合根的单进程桌面应用引入这些是纯负债。
