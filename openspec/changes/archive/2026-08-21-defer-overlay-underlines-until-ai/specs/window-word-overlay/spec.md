## MODIFIED Requirements

### Requirement: Word underline overlay
系统 SHALL 在目标窗口上方显示透明置顶覆盖层,并为每个本地分词词的**每个 rect** 在其对应行的词框下方绘制横线(跨行词因此有多条下划线,各贴各自行底);对每个已验证 phrase group,系统 SHALL 为其每个 part 的每个行内几何绘制可识别的组合词块标记。覆盖层刷新时 SHALL 整体替换当前词和 group 列表,隐藏时 SHALL 清除所有标记并恢复点击穿透。悬停一个词的**任一 rect** 时,系统 SHALL 高亮该词的**全部 rect**(整词一起变色);悬停 group 的任一 part 时,系统 SHALL 高亮该 group 的全部 parts。启用短语分析时，覆盖层 MAY 先显示本地振假名而暂不绘制下划线；分析批次完成后的刷新 SHALL 仅为成功句段中的词绘制下划线。关闭短语分析时 SHALL 保持原有本地下划线行为。

#### Scenario: Draw one underline per eligible word
- **WHEN** 当前会话包含一个或多个可下划线的本地分词词
- **THEN** 覆盖层为每个词的每个 rect 各绘制一条与词框宽度对齐的横线(跨行词每行一条),线条位于各自词框底部内侧且至少有 1px 可见线宽

#### Scenario: Draw one underline per word
- **WHEN** 当前会话包含一个或多个本地分词词
- **THEN** 覆盖层为每个词的每个 rect 各绘制一条与词框宽度对齐的横线(跨行词每行一条),线条位于各自词框底部内侧且至少有 1px 可见线宽

#### Scenario: Draw local word and phrase markers
- **WHEN** 当前会话包含本地词和一个或多个 phrase group
- **THEN** 覆盖层绘制本地词横线,并为 group 的所有 part 几何绘制对应标记

#### Scenario: Show furigana while analysis is pending
- **WHEN** 短语分析已启用但尚未完成
- **THEN** 覆盖层显示本地振假名,不绘制下划线或 group 标记

#### Scenario: Restrict lines after partial analysis
- **WHEN** 最终会话同时包含成功句段词和失败句段词
- **THEN** 只有成功句段词绘制下划线,失败句段词仍可显示振假名但没有下划线

#### Scenario: Preserve disabled-analysis behavior
- **WHEN** 短语分析未启用
- **THEN** 本地词和振假名按原有行为同时显示下划线

#### Scenario: Refresh and hide overlay
- **WHEN** 用户重新识别窗口或关闭覆盖层
- **THEN** 旧词线、振假名和 group 标记都不会残留;关闭后覆盖层不可见、不可激活且不拦截鼠标输入

#### Scenario: Change line color on hover
- **WHEN** 鼠标悬停在某个词的任一 rect 上
- **THEN** 该词的**全部 rect** 横线由默认色变为悬停色(整词一起高亮),其他词的横线保持默认色

#### Scenario: Restore color on mouse leave
- **WHEN** 鼠标移出悬停词的包围盒
- **THEN** 该词的全部 rect 横线恢复为默认色

#### Scenario: Highlight all parts of a group
- **WHEN** 鼠标悬停 phrase group 的任一 part 时
- **THEN** 该 group 的所有 part 几何同时进入悬停状态,并保持其他 group 的状态不变

#### Scenario: Overlay stays click-through while hovering
- **WHEN** 鼠标悬停在词热区或高亮 phrase group
- **THEN** 覆盖层整体仍不拦截点击,点击事件穿透到下方窗口
