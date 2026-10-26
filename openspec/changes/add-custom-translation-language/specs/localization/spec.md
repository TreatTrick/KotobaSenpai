# 界面翻译与分析指令分别处理

## RENAMED Requirements

- FROM: `### Requirement: 本地化器被非 View 消费方使用`
- TO: `### Requirement: 界面本地化与模型指令分别处理`

## MODIFIED Requirements

### Requirement: 界面本地化与模型指令分别处理

`IStringLocalizer` SHALL 继续定义在 Core、实现在 App，并供视图模型及其他需要界面语言文本的消费方使用；Core 和 Platform SHALL 不新增界面本地化资源或界面文化解析逻辑。本地化文本 SHALL 在调用时反映当前界面文化。模型分析指令 SHALL 改为独立的英文模板；模型的回答语言 SHALL 来自句段分析开始时读取的当前生效设置。设置页、弹窗标题、错误和状态提示 SHALL 继续按界面语言本地化。

#### Scenario: 界面消费方按当前语言解析资源
- **WHEN** 界面为英文，消费方通过 `IStringLocalizer.Get` 读取设置说明或状态资源
- **THEN** 返回对应英文资源值；现有中性英文回退规则继续适用

#### Scenario: 切换界面后刷新界面文本
- **WHEN** 界面语言从中文切换为英文
- **THEN** 设置标签、按钮、状态和弹窗标题刷新为英文；分析指令仍使用英文模板，不重新翻译整份提示词

#### Scenario: 回答语言可以独立于界面
- **WHEN** 界面为中文、自定义释义语言为“法语”
- **THEN** 弹窗标题和无释义提示仍为中文，模型的释义和语法说明按法语要求生成
