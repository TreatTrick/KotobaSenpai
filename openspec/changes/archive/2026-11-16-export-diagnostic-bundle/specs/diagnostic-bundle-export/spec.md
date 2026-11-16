## ADDED Requirements

### Requirement: 用户确认后导出本地诊断包

系统 SHALL 在设置页提供诊断包导出入口。用户触发后，系统 SHALL 在生成前显示醒目的确认对话框，并根据本次实际收集清单列出将打包的日志和诊断类别及文件数量。对话框 SHALL 说明 ZIP 只在本机生成且不会自动上传。用户取消确认或取消保存位置选择时，系统 SHALL NOT 生成 ZIP。

#### Scenario: 确认诊断包内容
- **WHEN** 用户在设置页触发导出且存在可打包文件
- **THEN** 系统在创建 ZIP 前显示本次文件类别和数量，并询问是否继续

#### Scenario: 用户取消导出
- **WHEN** 用户取消确认，或在确认后取消 ZIP 保存位置选择
- **THEN** 系统不创建 ZIP 文件

#### Scenario: 只有日志可用
- **WHEN** 当前没有诊断文件但存在日志文件
- **THEN** 系统仍允许导出，并在确认框说明未找到诊断文件

#### Scenario: 确认框遵循应用 FluentLight 主题
- **WHEN** 确认框作为独立窗口显示
- **THEN** 窗口使用当前应用明暗主题，提供自己的 `FluentLightHost`，并为表面和按钮设置匹配实际圆角的 FluentLight 配置

### Requirement: 诊断包只包含允许的本地诊断文件

系统 SHALL 将当前存在且名称匹配应用规则的日志文件和已知诊断文件加入 ZIP，并按 `logs/` 与 `diag/` 分类保存。日志文件 SHALL 匹配 `kotobasenpai-yyyy-MM-dd.log`；诊断文件 SHALL 使用应用写入的 `frame-`、`ocr-`、`tokens-`、`phrase-`、`llm-req-`、`llm-resp-`、`llm-groups-` 或 `llm-words-` 前缀。系统 SHALL NOT 将设置文件、凭据或其他不匹配的文件加入 ZIP。

#### Scenario: 包含现存的日志和诊断文件
- **WHEN** 日志目录和诊断目录中存在符合命名规则的文件
- **THEN** ZIP 包含这些文件，保留原文件名并分别放入 `logs/` 与 `diag/`

#### Scenario: 排除设置和未知文件
- **WHEN** 相关目录中存在设置文件、凭据或名称不匹配的文件
- **THEN** ZIP 不包含这些文件

#### Scenario: 导出写入失败
- **WHEN** 保存位置不可写或读取源文件失败
- **THEN** 系统向用户显示本地化失败提示，且不报告导出成功
