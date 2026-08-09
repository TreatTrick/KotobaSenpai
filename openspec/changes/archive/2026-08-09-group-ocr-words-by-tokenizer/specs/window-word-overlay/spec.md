## MODIFIED Requirements

### Requirement: Word underline overlay
系统 SHALL 在目标窗口上方显示透明置顶覆盖层，并为每个分词词在其词框下方绘制一条横线。覆盖层刷新时 SHALL 整体替换当前词列表，隐藏时 SHALL 清除所有横线并恢复点击穿透。当鼠标悬停在某个词的包围盒上时，该词下方整条横线 SHALL 由默认色变为悬停色，其余词的横线保持默认色；鼠标移出后恢复默认色。

#### Scenario: Draw one underline per word
- **WHEN** 当前会话包含一个或多个分词词
- **THEN** 覆盖层为每个分词词绘制一条与词框宽度对齐的横线，线条位于词框底部内侧且至少有 1px 可见线宽

#### Scenario: Refresh and hide overlay
- **WHEN** 用户重新识别窗口或关闭覆盖层
- **THEN** 旧词线不会残留；关闭后覆盖层不可见、不可激活且不拦截鼠标输入

#### Scenario: Change line color on hover
- **WHEN** 鼠标悬停在某个分词词的包围盒上
- **THEN** 该词下方整条横线由默认色变为悬停色，其他词的横线保持默认色

#### Scenario: Restore color on mouse leave
- **WHEN** 鼠标移出该分词词的包围盒
- **THEN** 该词下方横线恢复为默认色

#### Scenario: Overlay stays click-through while hovering
- **WHEN** 鼠标悬停在词热区上触发变色
- **THEN** 覆盖层整体仍不拦截点击，点击事件穿透到下方窗口