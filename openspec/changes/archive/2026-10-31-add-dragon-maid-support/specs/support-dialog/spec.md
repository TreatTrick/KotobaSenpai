## ADDED Requirements

### Requirement: 复刻 Clutch 打赏弹窗外观

系统 SHALL 按 Clutch 的 `SupportView.swift` 还原左角色、右支持卡片的布局，以及 22 DIP 外圆角、12 DIP 卡片圆角、轻描边、支付分段选择和圆形关闭按钮。弹窗 SHALL 使用与当前深浅主题一致的不透明实色背景。默认宽度约 494 DIP，角色区域约 154×280 DIP，二维码区域 112×112 DIP。反馈和感谢状态 SHALL 保持布局稳定。

#### Scenario: 打开未支持状态
- **WHEN** 用户点击左下角咖啡按钮
- **THEN** 显示原版风格的角色、累计识别次数、收款卡片、GitHub 入口和关闭按钮，累计次数为 0 时也显示

### Requirement: 保留原版进退场方式

支持弹窗 SHALL 为非模态窗口，同一时刻只有一个实例；重复打开时显示已有实例。自动提醒 SHALL 位于主窗口所在屏幕的工作区右下角，距工作区边缘约 20 DIP；用户点击咖啡入口时 SHALL 在主窗口中央显示并激活弹窗，位置 SHALL 限制在所在屏幕的工作区内。弹窗 SHALL 以 0.35 秒向上移动 24 DIP 并淡入，关闭时淡出 0.2 秒。自动提醒 SHALL 不抢走当前应用焦点。窗口 SHALL 可以点击操作，并支持获得焦点后的键盘操作和 Escape 关闭。关闭系统动画时 SHALL 立即显示或关闭。

#### Scenario: 自动提醒到达
- **WHEN** 下次启动的提醒检查允许展示，且用户尚未开始使用识别功能
- **THEN** 弹窗按原版节奏出现，不切走用户当前应用

#### Scenario: 重复点击入口
- **WHEN** 支持弹窗已打开，用户再次点击咖啡按钮
- **THEN** 将同一个弹窗移到主窗口中央并激活，不叠加新窗口

#### Scenario: 手动打开支持入口
- **WHEN** 尚未打开支持弹窗，用户点击咖啡按钮
- **THEN** 在主窗口中央显示并激活弹窗，位置限制在所在屏幕的工作区内

### Requirement: 直接复用原收款方式

系统 SHALL 原样使用 Clutch 的微信、支付宝和 PayPal 收款图，以及 `https://www.paypal.com/ncp/payment/GVHHQGRMT9YYJ` 链接。中文默认微信，英文默认 PayPal。二维码 SHALL 保留比例和清晰度，支付切换或打开 PayPal SHALL 不直接标记已支持。

#### Scenario: 切换支付方式
- **WHEN** 用户切换微信、支付宝和 PayPal
- **THEN** 显示各自的原始收款图与说明，PayPal 提供打开链接按钮

#### Scenario: 打开外部链接失败
- **WHEN** 系统无法打开 PayPal 或 GitHub
- **THEN** 弹窗显示可重试的错误，不进入庆祝状态，也不把失败操作记录为支持

### Requirement: 区分确认支持与访问 GitHub

系统 SHALL 在用户点击“我已支持”且保存成功后显示感谢标题、感谢卡片和跳跃动画，左下角按钮同步为已支持。系统 SHALL 允许再次查看收款码，GitHub 入口始终可用，目标为 `https://github.com/TreatTrick/KotobaSenpai`。打开 GitHub 成功后 SHALL 播放庆祝并提示页面已打开，不宣称已经 Star 或完成支付。

#### Scenario: 已支持后再次查看
- **WHEN** 已确认支持的用户重新打开弹窗并点击查看收款码
- **THEN** 可以继续扫码和访问 GitHub，感谢状态保留，不恢复自动提醒

### Requirement: Alt 点击入口重置调试状态

系统 SHALL 复刻 Clutch 的 Option 点击调试操作，在 Windows 使用 Alt。仅 Debug 构建 SHALL 允许按住 Alt 点击左下角“我已支持”／咖啡入口，取消本次启动的待检查项，清除支持行为、打赏确认、最后提醒日期和节点，保留识别次数、活跃日及其他设置。保存成功后 SHALL 同步恢复未支持的入口和角色状态，结束已有弹窗的庆祝并重新打开未支持状态弹窗，不消耗提醒节点；本次运行 SHALL NOT 恢复自动提醒。保存失败 SHALL 保留原状态并反馈错误。Debug 入口提示 SHALL 包含 Alt 重置说明；Release SHALL 不显示该说明，Alt 点击 SHALL 按普通点击处理，不重置任何状态。

#### Scenario: Debug 重置已支持状态
- **WHEN** 用户在 Debug 构建中按住 Alt 点击左下角“我已支持”，且重置保存成功
- **THEN** 按钮恢复金色咖啡入口，角色恢复未支持的鼠标互动，弹窗显示收款卡片；原识别次数和活跃日保留，支持声明与提醒进度清除，重启后仍为未支持

#### Scenario: Debug 重置后检查提醒
- **WHEN** 用户已通过 Alt 点击成功重置，保留的活跃日已达到提醒节点
- **THEN** 本次运行只手动显示未支持弹窗，下次启动才按提醒规则重新检查

#### Scenario: Release 中按住 Alt
- **WHEN** 用户在 Release 构建中按住 Alt 点击左下角“我已支持”入口
- **THEN** 正常打开支持弹窗，已支持状态、提醒记录和感谢动画保持不变

#### Scenario: 重置保存失败
- **WHEN** Debug 中 Alt 点击触发的重置无法保存
- **THEN** 反馈支持功能错误，原支持状态、提醒记录和界面保持不变，不宣称重置成功

### Requirement: 使用现有本地化和主题

所有新增界面文案 SHALL 提供英文和简体中文 `.resx` 资源，沿用 Clutch 原版语气，并将应用名称和次数说明改为 KotobaSenpai 及识别次数。弹窗 SHALL 跟随现有深浅主题，按钮、支付选项和反馈 SHALL 可通过键盘及辅助技术识别。语言或主题切换 SHALL 不重置支持状态或重复创建窗口。

#### Scenario: 切换语言和主题
- **WHEN** 用户切换中英文或深浅主题
- **THEN** 常驻入口和已打开的弹窗同步更新，弹窗背景保持不透明并使用当前主题实色，当前支付选项与支持状态保持不变
