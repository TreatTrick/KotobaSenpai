## ADDED Requirements

### Requirement: Fluent visual theme

系统 SHALL 对主窗口应用 Windows 11 Fluent 主题：以 `ui:FluentWindow` 承载，启用 Mica 云母背景与自定义标题栏，并以 WPF-UI 主题画刷渲染标准控件（Button、ComboBox、TextBlock），替换此前硬编码的颜色（`#DDD`/`#555`/`#333`）。

#### Scenario: Hardcoded colors replaced by theme brushes

- **WHEN** 主窗口以任一主题渲染
- **THEN** 边框、正文与状态文本使用 WPF-UI 主题画刷而非硬编码十六进制颜色

#### Scenario: Mica backdrop and custom title bar

- **WHEN** 主窗口在 Windows 11 上渲染
- **THEN** 标题栏与背景呈现 Mica 云母融合观感（Win10 自动回退为纯色），标题经 `ui:TitleBar` 显示

#### Scenario: Themed controls

- **WHEN** 用户与主窗口中的按钮/下拉框交互
- **THEN** 控件呈现 WPF-UI Fluent 样式与悬停/按下反馈

### Requirement: Settings card layout

系统 SHALL 以 Win11 设置卡片布局组织主窗口内容：用 `ui:Card` 分组"目标窗口"、"外观"、"操作"等区域，每行为左侧标签 + 右侧控件。

#### Scenario: Grouped settings in cards

- **WHEN** 主窗口渲染
- **THEN** 目标窗口选择、外观（主题/语言）、操作按钮分别置于带标题的卡片容器内

### Requirement: Theme modes with persistence and OS following

系统 SHALL 支持三种主题模式：`Auto`（跟随 Windows 系统主题）、`Light`、`Dark`，可在运行时切换；所选模式 SHALL 持久化到 `%LocalAppData%/KotobaSenpai/settings.json` 并在启动时恢复。`Auto` 模式 SHALL 经 WPF-UI `SystemThemeWatcher` 跟随系统主题并在系统主题变化时即时跟随；`Light`/`Dark` SHALL 停止跟随直至重新选择 `Auto`。

#### Scenario: Runtime mode switch

- **WHEN** 用户在外观卡片切换主题模式（`Auto`/`Light`/`Dark`）
- **THEN** 主窗口按所选模式即时应用对应主题，无需重启

#### Scenario: Preference persists across restart

- **WHEN** 用户选择一个模式后关闭并重新启动应用
- **THEN** 应用以用户上次选择的模式启动

#### Scenario: Default mode on first run

- **WHEN** `settings.json` 不含主题字段或字段非法
- **THEN** 应用回退到默认 `Auto` 模式启动

#### Scenario: Auto mode follows OS theme at startup

- **WHEN** 应用以 `Auto` 模式启动且 Windows 当前为深色（或浅色）主题
- **THEN** 应用以深色（或浅色）Fluent 主题启动

#### Scenario: Auto mode follows OS theme change at runtime

- **WHEN** 应用运行于 `Auto` 模式且用户在 Windows 切换系统深浅主题
- **THEN** 应用经 `SystemThemeWatcher` 即时跟随切换，无需重启

#### Scenario: Manual override stops following OS

- **WHEN** 用户在 `Auto` 模式下显式选择 `Light` 或 `Dark`
- **THEN** 应用固定为所选主题（`UnWatch`），不再跟随系统主题变化，直至重新选择 `Auto`

### Requirement: Localization preserved on restyled controls

Fluent 主题重构 SHALL NOT 改变既有本地化行为：本地化标签 SHALL 继续经 `{loc:Loc}` 扩展解析，并在切换 UI 文化时即时更新。

#### Scenario: Localized labels render on Fluent controls

- **WHEN** 主窗口以任一主题渲染
- **THEN** 所有标签经 `{loc:Loc}` 解析为当前 UI 文化的文案

#### Scenario: Culture switch live-updates Fluent labels

- **WHEN** 用户切换 UI 文化
- **THEN** 已渲染的 Fluent 控件标签（含主题模式 ComboBox 项）即时更新为新文化文案，无需重启
