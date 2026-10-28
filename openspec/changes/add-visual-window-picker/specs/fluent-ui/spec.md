## MODIFIED Requirements

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
