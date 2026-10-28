# fluent-ui Specification

## Purpose
TBD - created by archiving change add-fluent-ui. Update Purpose after archive.
## Requirements
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

系统 SHALL 以 Win11 设置卡片布局组织主窗口内容，分组目标窗口、外观和操作区域。目标窗口卡片 SHALL 使用正方形预览、窗口标题、状态和选择 / 重新选择按钮；原目标窗口下拉列表与刷新按钮 SHALL 移除。其他卡片 SHALL 保留现有左侧标签、右侧控件的布局。预览 SHALL 按原比例完整显示客户区画面，空余区域使用主题背景填充，不拉伸或裁掉画面；空间不足时 SHALL 允许纵向排列。

#### Scenario: Grouped settings in cards

- **WHEN** 主窗口渲染
- **THEN** 目标窗口、外观和操作仍分别置于带标题的卡片容器，目标卡片使用预览布局，其他卡片保持现有分组和控件布局

#### Scenario: Render an unselected target card

- **WHEN** 当前没有已选目标
- **THEN** 目标卡片显示正方形占位区域、窗口图标、选择提示和选择按钮，不显示窗口下拉列表或刷新按钮

#### Scenario: Render a selected widescreen target

- **WHEN** 目标客户区截图为宽屏或其他非正方形比例
- **THEN** 外框保持正方形，图片等比例完整适配并以主题背景留白，旁边显示窗口标题、状态和重新选择按钮

#### Scenario: Keep status within the target card

- **WHEN** 预览获取失败、目标暂不可用或目标已关闭
- **THEN** 目标卡片内显示对应提示及可用的重试 / 重新选择入口，已关闭或替换的目标不继续作为有效预览显示

#### Scenario: Localize and expose the selection controls

- **WHEN** 用户切换中文 / 英文、深浅主题或使用键盘与辅助技术访问目标卡片
- **THEN** 提示和控件名称按当前界面语言更新，卡片与留白使用当前主题，选择按钮可通过键盘操作，窗口标题与状态可被辅助技术读取，静态截图不因这些变化重新获取

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

