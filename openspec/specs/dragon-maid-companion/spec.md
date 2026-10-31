# dragon-maid-companion Specification

## Purpose

Define the bundled dragon maid companion, persistent main-window entry, mouse interactions, celebration animation, and accessible playback lifecycle.

## Requirements

### Requirement: 使用 Clutch 原版角色动画

系统 SHALL 使用 Clutch 的 `MascotA.heic`、`MascotB.heic` 转换出的透明图片帧，保留原角色、画布、帧顺序和 24 fps 速度。素材 SHALL 随软件打包，安装后不依赖 Clutch 目录或额外解码器。

#### Scenario: 运行发布版
- **WHEN** 用户在没有 Clutch 项目的电脑上运行发布版
- **THEN** 龙女仆仍完整显示，透明边缘、表情和跳跃动作与原版一致

### Requirement: 在主界面左下角放大显示

系统 SHALL 在主界面左侧栏底部常驻显示龙女仆，下方放置 Clutch 风格的金色咖啡按钮。角色默认约 300 DIP 高，保持比例；小窗口时 SHALL 缩小到不遮挡导航和按钮。角色区域 SHALL 在识别页和设置页共用，首次引导期间不显示。

#### Scenario: 切换主界面页面
- **WHEN** 用户在识别页和设置页之间切换
- **THEN** 左下角角色与咖啡按钮持续可见，支持状态保持一致

#### Scenario: 缩小窗口
- **WHEN** 用户把窗口缩至 900×620 或使用较高屏幕缩放
- **THEN** 角色保持完整比例，导航、咖啡按钮和主要功能仍可操作

### Requirement: 还原鼠标驱动的表情变化

系统 SHALL 按 Clutch 桌面版播放 A 动画：常驻角色在鼠标靠近咖啡按钮时变开心，远离时变委屈；弹窗角色仅在右上角 1/16 区域内靠近关闭按钮时变委屈，离开该区域时变开心。系统 SHALL 保留 3 DIP 方向变化门槛、端点停留和轻微呼吸效果，窗口移动不应改变鼠标接近方向。

#### Scenario: 靠近咖啡按钮
- **WHEN** 鼠标逐渐靠近咖啡按钮，然后远离
- **THEN** 角色从当前帧分别向开心、委屈方向播放，不突然跳回起始帧

#### Scenario: 靠近关闭按钮
- **WHEN** 鼠标在弹窗右上区域靠近关闭按钮，随后移向支付或 GitHub 区域
- **THEN** 弹窗角色先变委屈，再向开心方向播放，关闭按钮始终可以使用

### Requirement: 保留支持后的跳跃庆祝

系统 SHALL 在成功打开 GitHub 或确认支持时，让弹窗角色先正放完 A，再循环播放 B。确认支持后，左下角角色 SHALL 同步进入庆祝；重启或重新打开弹窗时 SHALL 保留已确认支持的感谢状态。

#### Scenario: 确认支持
- **WHEN** 用户点击“我已支持”且状态保存成功
- **THEN** 左下角和弹窗角色进入跳跃庆祝，重新打开后仍显示感谢状态

#### Scenario: 调试重置支持状态
- **WHEN** 用户在 Debug 构建中按住 Alt 点击左下角入口，且重置保存成功
- **THEN** 左下角和弹窗角色结束庆祝，恢复未支持时的鼠标互动

### Requirement: 正确暂停和恢复动画

系统 SHALL 在对应视图隐藏、卸载或关闭时停止播放，常驻角色还 SHALL 在主窗口最小化或应用失去活动状态时暂停。恢复时 SHALL 从当前状态继续，不补播暂停期间的时间。Windows 关闭动画或启用高对比度时 SHALL 显示开心／感谢静帧。素材异常 SHALL 使用静态回退，不影响主功能。

#### Scenario: 最小化后恢复
- **WHEN** 用户最小化主窗口一段时间后恢复
- **THEN** 隐藏期间不持续解码动画，恢复后播放平稳，不快速跳帧

#### Scenario: 关闭系统动画
- **WHEN** Windows 动画被关闭或启用高对比度
- **THEN** 角色显示相应静帧，咖啡入口和支持确认仍正常工作
