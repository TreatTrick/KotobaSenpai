## ADDED Requirements

### Requirement: 整词优先的分层离线音调解析
系统 SHALL 在确定每个词块的最终有效读音后（包括首次本地分组以及 LLM 返回经校验的语境读音纠偏之后），按以下优先级在本地离线解析该词块的整词音调（`SpanPitch`）：
1. 项目人工校正表，优先按 `(原始表记, 平假名有效读音)` 精确匹配，未命中时再尝试适用的汉数字规范化表记；
2. 离线 Kanjium SQLite 词典（`pitch-accent.db`），按 `(表记, 平假名有效读音)` 精确匹配（含对阿拉伯数字表记的汉数字规范化查表）；
3. 高确定性白名单助数词规则（`週間`、`時間`、`年生`、`ヶ月` 变体、`曜日`）。

整词查询 MUST 仅使用词块的出现形 `Surface` 与最终有效读音，不得将 `LookupKey` 辞书形的词典音调直接套用到活用出现形（例如不得将 `食べる[2]` 套用到 `食べて`）。整词解析命中的全部合法候选核位置 SHALL 按来源顺序保留在 `SpanPitch.Positions` 中，并以第一个候选作为主展示位置；解析结果 SHALL 仅用于本地展示与诊断，不得加入 LLM 请求体。

#### Scenario: 一週間经离线词典解析出整词音调
- **WHEN** `一週間`（或 `1週間`）经有效读音解析确定为 `いっしゅうかん`
- **THEN** 分层解析通过 Kanjium 精确命中整词 `SpanPitch`，位置为 `[3]`，来源为 `Dictionary`

#### Scenario: 三週間经白名单助数词规则解析出整词音调
- **WHEN** `三週間` 经词块分组确定有效读音为 `さんしゅうかん`，且人工校正表与离线词典均无整词记录
- **THEN** 分层解析回退至白名单助数词规则，产出整词 `SpanPitch`，位置为 `[3]`，来源为 `CounterRule`

#### Scenario: LLM 纠偏读音后重新解析整词音调
- **WHEN** `額` 的有效读音被 LLM 从 `がく` 纠偏为 `ひたい`，且离线词典收录 `額（ひたい）` 的整词音调
- **THEN** 系统以 `("額", "ひたい")` 重新运行分层解析并更新为新读音对应的整词 `SpanPitch`，不再停留在音调未知或仅单 token 参考

#### Scenario: 活用形不误用辞书形整词音调
- **WHEN** 词块 `Surface` 为 `食べて`、`LookupKey` 为 `食べる`、有效读音为 `たべて`，离线词典仅收录 `食べる` 而未收录 `食べて`
- **THEN** 整词查询不以 `食べる` 查表，不产生错误的 `SpanPitch`，仍回落至既有源 token 处理路径

## MODIFIED Requirements

### Requirement: 在本地传播有序音调片段
系统 SHALL 在 `GroupedWord` 保留按源 UniDic token 顺序排列的音调片段与已解析的整词音调（`SpanPitch`）。`WordOverlaySession` SHALL 从 `GroupedWord` 将 `SpanPitch` 与 `WordPitch` 汇入本地 `WordMeaningView`。句段 token 引用和本地合并词摘要 MUST NOT 携带 `SpanPitch`。未发生读音覆盖且未解析出 `SpanPitch` 时，合并词的显示 token 不得覆盖或替代源 token 的音调序列。当词块解析出有效的 `SpanPitch` 时，展示层 SHALL 优先采用 `SpanPitch` 作为该词块的整体音调。当词块的最终有效读音被 JMdict 词典回流、助数词推导或 LLM 语境纠偏覆盖、且未能解析出有效的整词 `SpanPitch` 时，系统 MUST 将该词块对应的源 token 展示音调片段置为空（视为整组音调未知），不得把 UniDic 原同形异音词的过期音调核套用到新读音上。所有音调数据 SHALL 只用于本地渲染、详情展示和本地诊断，不得自动加入 LLM provider payload。

#### Scenario: 合并词未命中整词音调时保留每个源 token 的音调
- **WHEN** 一个本地合并词由两个 UniDic token 组成、未触发读音覆盖、未命中整词 `SpanPitch`，且两个 token 的 `aType` 不同
- **THEN** 合并词仍能按源 token 顺序取得两份音调片段，渲染和详情不会只使用第一个 token

#### Scenario: 读音被覆盖后若命中整词音调则传播整词音调
- **WHEN** `一週間` 的有效读音由 JMdict 覆盖为 `いっしゅうかん`，且整词分层解析命中 `SpanPitch([3])`
- **THEN** `GroupedWord` 与 `WordMeaningView` 传播该整词 `SpanPitch`，覆盖层与弹窗均按整词 `[3]` 展示

#### Scenario: 读音被覆盖且未命中整词音调时抑制过期音调
- **WHEN** `頭を垂れる` 的有效读音由 JMdict 从 `あたまをたれる` 覆盖为 `こうべをたれる`，且整词分层解析未命中整词 `SpanPitch`
- **THEN** 该词的整组音调视为未知，不套用原 `あたま[2]` 的高低音着色、圆点或数字标记

#### Scenario: 整词音调经会话汇入释义视图
- **WHEN** 一个句段被构建为 LLM 分析请求
- **THEN** `SpanPitch` 仅沿本地会话路径进入 `WordMeaningView`。未覆盖词的源 token 音调仍可用于本地展示，provider 请求体保持既有语义字段

#### Scenario: 缺少音调时保留其他本地数据
- **WHEN** 某个 token 没有有效 `aType` 且词块未命中整词 `SpanPitch`，或因读音覆盖且无整词命中导致音调未知
- **THEN** surface、有效 reading、POS、词义和几何仍然可用，仅缺少有效音调的部分表现为未知

### Requirement: 读音纠偏后回查 UniDic 单词音调
对于 JMdict 或 LLM 纠偏后的有效读音，系统 SHALL 先执行整词优先的分层离线音调解析；仅当未能解析出整词 `SpanPitch` 时，如果读音变化可以唯一对齐到一个源词元，系统 SHALL 使用该词元的原表记、新读音、词性和活用信息匹配 UniDic 词条并记录为单 token 单词参考（`WordPitch`）。悬停弹窗和短语成员词表 SHALL 将此类结果标为 UniDic 单词音调，并保留已查到的有效音调候选；没有可靠对齐或匹配时 SHALL 显示音调未知。单 token 单词参考 SHALL 仅用于本地详情，不得充当整组 `SpanPitch`，覆盖层的整组振假名继续使用普通颜色。

#### Scenario: 整词未命中时悬停显示纠偏读音对应的单词音调参考
- **WHEN** `頭を垂れる` 的有效读音为 `こうべをたれる`，整词分层解析未命中整词音调，且 UniDic 的 `頭`（`こうべ`）候选音调为 `0,3`
- **THEN** 弹窗显示标有 UniDic 来源的 `頭（こうべ）` 单词音调及两个候选，覆盖层显示单色振假名

#### Scenario: 对齐不明确且整词未命中时保持音调未知
- **WHEN** 纠偏读音未命中整词音调、且无法唯一分配给一个原词元或 UniDic 未返回有效匹配的音调
- **THEN** 弹窗显示音调未知，仍展示纠偏后的有效读音
