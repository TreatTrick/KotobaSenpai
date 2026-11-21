## MODIFIED Requirements

### Requirement: 在本地传播有序音调片段
系统 SHALL 在 `LookupSpan`/`GroupedWord`、句段 token 引用、本地合并词摘要和释义视图之间保留按源 UniDic token 顺序排列的音调片段。未发生读音覆盖时，合并词的显示 token 不得覆盖或替代源 token 的音调序列。当词块的最终有效读音被 JMdict 词典回流或 LLM 语境纠偏覆盖、从而偏离 UniDic 源 token 读音时，系统 MUST 将该词块对应的展示音调片段置为空（视为音调未知），不得把 UniDic 原同形异音词的过期音调核套用到新读音上。音调片段 SHALL 只用于本地渲染和详情展示，不得自动加入 LLM provider payload。

#### Scenario: 合并词保留每个源 token 的音调
- **WHEN** 一个本地合并词由两个 UniDic token 组成、未触发读音覆盖，且两个 token 的 `aType` 不同
- **THEN** 合并词仍能按源 token 顺序取得两份音调片段，渲染和详情不会只使用第一个 token

#### Scenario: 读音被 JMdict 或 LLM 覆盖时抑制过期音调
- **WHEN** `額` 的读音由 LLM 从 `がく` 覆盖为 `ひたい`，或 `頭を垂れる` 的读音由 JMdict 从 `あたまをたれる` 覆盖为 `こうべをたれる`
- **THEN** 该词在覆盖层和悬停弹窗中的音调均视为未知，不套用原 `がく[0]` 或 `あたま[2]` 的高低音着色、圆点或数字标记

#### Scenario: 句段引用携带本地音调
- **WHEN** 一个句段被构建为 LLM 分析请求
- **THEN** 本地 token 引用和后续 phrase/word 视图可以从同一组 UniDic token 恢复未覆盖词的音调，而 provider 请求体仍保持既有语义字段

#### Scenario: 缺少音调时保留其他本地数据
- **WHEN** 某个 token 没有有效 `aType` 或因读音覆盖导致音调未知
- **THEN** surface、有效 reading、POS、词义和几何仍然可用，只有该词的音调展示为未知
