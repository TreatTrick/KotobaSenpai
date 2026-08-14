## MODIFIED Requirements

### Requirement: Word underline overlay
系统 SHALL 在目标窗口上方显示透明置顶覆盖层，并为每个本地分词词在其词框下方绘制横线；对每个已验证 phrase group，系统 SHALL 为其每个 part 的每个行内几何绘制可识别的组合词块标记。覆盖层刷新时 SHALL 整体替换当前词和 group 列表，隐藏时 SHALL 清除所有标记并恢复点击穿透。悬停 group 的任一 part 时，系统 SHALL 高亮该 group 的全部 parts；普通词悬停行为保持不变。

#### Scenario: Draw one underline per word
- **WHEN** 当前会话包含一个或多个分词词
- **THEN** 覆盖层为每个分词词绘制一条与词框宽度对齐的横线，线条位于词框底部内侧且至少有 1px 可见线宽

#### Scenario: Draw local word and phrase markers
- **WHEN** 当前会话包含本地词和一个或多个 phrase group
- **THEN** 覆盖层绘制本地词横线，并为 group 的所有 part 几何绘制对应标记

#### Scenario: Refresh and hide overlay
- **WHEN** 用户重新识别窗口或关闭覆盖层
- **THEN** 旧词线和 group 标记都不会残留；关闭后覆盖层不可见、不可激活且不拦截鼠标输入

#### Scenario: Change line color on hover
- **WHEN** 鼠标悬停在某个本地分词词的包围盒上
- **THEN** 该词下方整条横线由默认色变为悬停色，其他词的横线保持默认色

#### Scenario: Restore color on mouse leave
- **WHEN** 鼠标移出该分词词的包围盒
- **THEN** 该词下方横线恢复为默认色

#### Scenario: Highlight all parts of a group
- **WHEN** 鼠标悬停在 phrase group 的任一 part 几何上
- **THEN** 该 group 的所有 part 几何同时进入悬停状态，并保持其他 group 的状态不变

#### Scenario: Overlay stays click-through while hovering
- **WHEN** 鼠标悬停在词热区或高亮 phrase group
- **THEN** 覆盖层整体仍不拦截点击，点击事件穿透到下方窗口

## ADDED Requirements

### Requirement: Resolve overlapping group hover deterministically
系统 SHALL 允许不同 phrase group 的几何重叠。当光标命中多个 group 时，系统 SHALL 优先选择引用 token 总数更少的 group；相同长度时 SHALL 选择 provider 返回顺序更靠前的 group。选中的 group SHALL 使用其应用 group ID 更新一个详情面板。

#### Scenario: Prefer the more specific group
- **WHEN** 光标同时命中一个短 group 和一个包含它的长 group
- **THEN** 系统显示短 group 的详情，并高亮短 group 的全部 parts

#### Scenario: Tie by provider order
- **WHEN** 多个命中 group 引用 token 数相同
- **THEN** 系统选择 provider 响应中更早出现的 group
