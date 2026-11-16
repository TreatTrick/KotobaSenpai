## Context

当前 App 将每日日志保存在 `%LocalAppData%/KotobaSenpai/logs/`，将可选识别诊断保存在 `%LocalAppData%/KotobaSenpai/diag/`。日志级别可以在设置页修改；未配置时目前默认为 Error。用户需要在遇到问题后快速整理已有资料。

## Goals / Non-Goals

**Goals:**
- 在设置页提供生成诊断 ZIP 的入口。
- 生成前显示醒目的本地化确认框，列出本次实际找到的资料类别和文件数量；取消时不生成。
- ZIP 仅包含符合应用命名规则的日志和已知诊断产物，不包含设置文件或凭据。
- 未显式配置日志级别时默认 Debug，已有用户设置继续优先。

**Non-Goals:**
- 不启用默认关闭的 `DiagEnabled`。
- 不上传 ZIP，不自动发送错误报告。
- 不加入崩溃弹窗入口、清理策略或新的日志系统。

## Decisions

- 使用 .NET 标准 `System.IO.Compression.ZipFile` 生成 ZIP，不增加依赖。
- 从现有 logs/diag 目录按文件名白名单收集当前存在的文件；确认文案基于同一份文件清单生成，导出也使用该清单。
- 在设置页日志区域放置按钮。先确认具体内容，再让用户选择 ZIP 保存位置；取消任一步骤都不生成 ZIP。
- 确认、成功和失败反馈使用同一个应用内模态窗口；窗口使用动态 Fluent 主题资源、自有 `FluentLightHost`、8 DIP 表面圆角和匹配的 4 DIP 按钮圆角。
- `SettingsPageViewModel` 负责收集允许的文件、生成本地化内容清单并调用 ZIP 导出器；`SettingsPage` code-behind 只处理确认窗口、保存文件对话框和提示窗口。
- 确认框按类别列出文件数。没有诊断文件时明确显示未找到诊断文件；只存在日志时仍允许导出。
- ZIP 内使用 `logs/` 和 `diag/` 相对目录结构，沿用原文件名，方便按识别 ID 关联。
- 把无配置回退值从 Error 改为 Debug；显式配置值和无效值回退以外的行为不变，无效值也回退到 Debug。

## Risks / Trade-offs

- 文件可能在确认后、写入 ZIP 前被删除或占用 → 导出失败时显示本地化错误，不留下成功状态；源文件不做修改。
- 每个诊断类别保留数量有限，且诊断默认关闭 → ZIP 只能包含生成前实际存在的文件；确认框列出实际清单。

## Migration Plan

无需迁移。已有 `MinimumLogLevel` 设置保持原值；只有缺失或无法解析时使用 Debug。

## Open Questions

无。
