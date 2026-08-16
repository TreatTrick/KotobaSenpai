## ADDED Requirements

### Requirement: Indicate group membership by member-word underline on hover
系统 SHALL NOT 为语法组合组绘制独立的上划线。组的成员关系 SHALL 通过高亮其成员本地合并词的下划线来呈现：当某个组被悬停/选中时，覆盖该组 parts 所引用 token 的本地合并词 SHALL 将其下划线渲染为高亮样式，并显示该组的详情弹窗；组未被悬停时，成员词 SHALL 仅显示普通下划线，不呈现任何组信号。

#### Scenario: Highlight member words on hover
- **WHEN** 一个语法组合组被悬停/选中
- **THEN** 覆盖该组 parts token 的本地合并词下划线被高亮，并显示该组详情弹窗

#### Scenario: No overline drawn
- **WHEN** 存在一个已验证的语法组合组
- **THEN** 任何时刻都不在其 token 上方绘制独立上划线

#### Scenario: Rest shows no group signal
- **WHEN** 没有组被悬停
- **THEN** 成员词仅显示各自普通的本地下划线，不呈现组信号