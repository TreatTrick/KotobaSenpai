# pitch-accent-display Specification

## Purpose
定义 UniDic 音调信息从分词结果到本地振假名、假名音调标记、词语释义和短语详情的保存、传播与展示契约。

## Requirements
### Requirement: 规范化并保留 UniDic 音调
系统 SHALL 在每个 UniDic token 上同时保留原始 `aType` 字符串和可空的规范化音调核位置。规范化位置 SHALL 使用 `0` 表示平板型、`1` 表示头高型、`N` 表示第 N 拍后下降；空值、`*`、负数、非数字或无法验证的值 SHALL 表示音调未知。多值 `aType` SHALL 选择第一个可解析候选，但不得丢弃原始多值字符串。

#### Scenario: 保留单值音调
- **WHEN** UniDic token 的 `aType` 为 `2`
- **THEN** token 保留原始字符串 `2`，并提供规范化音调核位置 `2`

#### Scenario: 处理多值音调
- **WHEN** UniDic token 的 `aType` 为 `0,2` 或等价的带引号 CSV 值
- **THEN** token 保留完整解码后的原始值，并使用第一个可解析候选 `0` 作为本地展示位置

#### Scenario: 未知音调不伪造结果
- **WHEN** UniDic token 的 `aType` 为空、为 `*` 或不是合法数字
- **THEN** token 的规范化音调为未知，后续 UI 不得把它当作低音或平板型绘制

### Requirement: 按读音拍计算高低模式
系统 SHALL 根据 token 的出现形读音拆分拍，并根据规范化音调核生成与拍一一对应的高/低模式。小假名 SHALL 与前一个假名组成一拍；`っ`、`ん` 和长音 SHALL 各自计为一拍。计算结果 SHALL 提供统一的可读 notation，例如 `[2] LH↓L`，供覆盖层和释义详情复用。

#### Scenario: 计算平板型
- **WHEN** 一个三拍读音的音调核为 `0`
- **THEN** 高低模式为 `L H H`，且 notation 标明音调核为 `0` 而不插入词内下降箭头

#### Scenario: 计算头高型
- **WHEN** 一个三拍读音的音调核为 `1`
- **THEN** 高低模式为 `H L L`

#### Scenario: 计算中高或尾高型
- **WHEN** 一个四拍读音的音调核为 `2` 或 `4`
- **THEN** `2` 生成 `L H L L`，`4` 生成 `L H H H`，并在 notation 中保留下降位置/尾高标记

#### Scenario: 合并小假名为一拍
- **WHEN** 读音为 `きょう`
- **THEN** 拍序列为 `きょ`、`う`，而不是四个独立字符

#### Scenario: 音调核超出拍数
- **WHEN** 规范化音调核大于读音的拍数
- **THEN** 该 token 的音调模式被标记为未知，系统不绘制错误的红蓝标记

### Requirement: 在本地传播有序音调片段
系统 SHALL 在 `LookupSpan`/`GroupedWord`、句段 token 引用、本地合并词摘要和释义视图之间保留按源 UniDic token 顺序排列的音调片段。合并词的显示 token 不得覆盖或替代源 token 的音调序列。音调片段 SHALL 只用于本地渲染和详情展示，不得自动加入 LLM provider payload。

#### Scenario: 合并词保留每个源 token 的音调
- **WHEN** 一个本地合并词由两个 UniDic token 组成，且两个 token 的 `aType` 不同
- **THEN** 合并词仍能按源 token 顺序取得两份音调片段，渲染和详情不会只使用第一个 token

#### Scenario: 句段引用携带本地音调
- **WHEN** 一个句段被构建为 LLM 分析请求
- **THEN** 本地 token 引用和后续 phrase/word 视图可以从同一组 UniDic token 恢复音调，而 provider 请求体仍保持既有语义字段

#### Scenario: 缺少音调时保留其他本地数据
- **WHEN** 某个 token 没有有效 `aType`
- **THEN** surface、reading、POS、词义和几何仍然可用，只有该 token 的音调片段为未知
