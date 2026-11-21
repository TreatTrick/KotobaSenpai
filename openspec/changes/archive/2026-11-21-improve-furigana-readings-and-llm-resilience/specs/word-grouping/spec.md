## MODIFIED Requirements

### Requirement: Token-boundary dictionary spans
系统 SHALL 在 UniDic 输出的完整 token 边界上生成连续候选词块，不得从 token 中间开始或结束；候选 SHALL 在一次识别内批量查询词典，并按从左到右的最长覆盖选择非重叠 span。对于包含至少一个汉字的多 token 候选，除精确拼接各 token `Surface` 外，当末尾 token 为可活用动词或形容词且其 `Surface` 与辞书形（`OrthBase`/`Lemma`）不同时，系统 SHALL 额外生成“前缀 `Surface` + 末尾 token 辞书形”作为词典候选；命中后 SHALL 按既有活用延续规则吸收紧随其后的助动词及接续助词 `て`/`で`，使单个 span 覆盖完整出现形，并以命中的辞书形作为 `LookupKey`。纯假名多 token 序列 SHALL 不触发末尾辞书形归一化匹配，以避免误合并普通助词与活用短串。

#### Scenario: Prevent a cross-token false match
- **WHEN** token 序列为 `も`、`ちゃんと`，且词典存在 `もち` 但不存在 `もちゃんと`
- **THEN** 系统不得生成 `もち` span；`も` 与 `ちゃんと` 各自保持为独立 span

#### Scenario: Merge a dictionary word across UniDic tokens
- **WHEN** token 序列为 `で`、`も`，且词典存在 `でも`
- **THEN** 系统生成一个覆盖两个 token 的 `でも` span，不同时生成重叠的 `で` span

#### Scenario: Merge a conjugated multi-token idiom with trailing lemma normalization
- **WHEN** token 序列为 `踵`、`を`、`返し`（`OrthBase=返す`）、`て`，且词典存在 `踵を返す`
- **THEN** 系统生成一个覆盖全部 4 个 token 的 `踵を返して` span，其 `LookupKey` 为 `踵を返す`，不将其拆成独立片段；`頭を垂れた` 命中 `頭を垂れる` 同理

#### Scenario: Pure-kana tokens do not trigger conjugated multi-token normalization
- **WHEN** 连续纯假名 token 序列（如 `に`、`し`、`た`）中末尾 token 发生活用变形
- **THEN** 系统不为该纯假名序列生成末尾辞书形多词候选，避免误命中无关假名词条

#### Scenario: Share the resolved span with lookup and underline
- **WHEN** 一个候选 span 在识别阶段已命中或确认未命中词典
- **THEN** 下划线热区、悬停弹窗和诊断 SHALL 使用同一个 span 结果，不得在悬停时从单个字符重新猜词

#### Scenario: Resolve inflection chains with UniDic lemma
- **WHEN** UniDic 输出一个有词典 lemma 的动词/形容词，后面紧跟活用助动词（如 `なかっ` + `た`）
- **THEN** 系统以基础 lemma 查词，同时让 span 覆盖完整出现形 `なかった`

#### Scenario: Hard boundaries
- **WHEN** 候选 token 之间存在标点、空白或非连续字符偏移
- **THEN** 系统不得跨越该边界合并词块
