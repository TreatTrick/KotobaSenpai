# word-grouping Specification

## Purpose
TBD - created by archiving change group-ocr-words-by-tokenizer. Update Purpose after archive.
## Requirements
### Requirement: Group OCR characters into tokenizer words
系统 SHALL 将一行 OCR 识别出的字符（按阅读顺序、含字符框）经分词器重新组合成词，每个分词对应一个"词"，并为该词计算其所有成员字符框的并集包围盒，作为该词的下划线几何。

#### Scenario: Group a line of characters into tokens
- **WHEN** 某一行的 OCR 字符文本经分词器切分后得到若干 token
- **THEN** 系统为每个 token 计算一个词，词的包围盒为其全部成员字符框的并集

#### Scenario: Token spans map back to character boxes
- **WHEN** token 在行文本中的起始偏移与长度已知
- **THEN** 系统把组成该 token 的字符框聚合为一个词，保证词框覆盖所有成员字符

#### Scenario: Word with no characters
- **WHEN** 一个 token 没有映射到任何有效字符框
- **THEN** 系统跳过该 token，不生成词或横线

### Requirement: Token scope for underlining
系统 SHALL 为分词结果中"全部词（含助词/助动词）"生成词，但排除标点符号与空白类 token。

#### Scenario: Include particles and auxiliaries
- **WHEN** 分词结果包含助词或助动词（如 は、を、です）
- **THEN** 系统为这些 token 生成词并画线

#### Scenario: Exclude punctuation and whitespace
- **WHEN** 分词结果包含标点符号或空白类 token
- **THEN** 系统不为其生成词或画线

### Requirement: One underline geometry per token
系统 SHALL 为每个分词词恰好生成一条下划线几何，该几何跨越该词全部成员字符框的宽度，而不是按成员字符逐条生成。

#### Scenario: Multi-character word has a single line
- **WHEN** 一个词由多个字符构成
- **THEN** 该词只产生一条横线，横线宽度等于词包围盒宽度，而不是每个字符一条线

### Requirement: Token-boundary dictionary spans
系统 SHALL 在 UniDic 输出的完整 token 边界上生成连续候选词块，不得从 token 中间开始或结束；候选 SHALL 在一次识别内批量查询词典，并按从左到右的最长覆盖选择非重叠 span。

#### Scenario: Prevent a cross-token false match
- **WHEN** token 序列为 `も`、`ちゃんと`，且词典存在 `もち` 但不存在 `もちゃんと`
- **THEN** 系统不得生成 `もち` span；`も` 与 `ちゃんと` 各自保持为独立 span

#### Scenario: Merge a dictionary word across UniDic tokens
- **WHEN** token 序列为 `で`、`も`，且词典存在 `でも`
- **THEN** 系统生成一个覆盖两个 token 的 `でも` span，不同时生成重叠的 `で` span

#### Scenario: Share the resolved span with lookup and underline
- **WHEN** 一个候选 span 在识别阶段已命中或确认未命中词典
- **THEN** 下划线热区、悬停弹窗和诊断 SHALL 使用同一个 span 结果，不得在悬停时从单个字符重新猜词

#### Scenario: Resolve inflection chains with UniDic lemma
- **WHEN** UniDic 输出一个有词典 lemma 的动词/形容词，后面紧跟活用助动词（如 `なかっ` + `た`）
- **THEN** 系统以基础 lemma 查词，同时让 span 覆盖完整出现形 `なかった`

#### Scenario: Hard boundaries
- **WHEN** 候选 token 之间存在标点、空白或非连续字符偏移
- **THEN** 系统不得跨越该边界合并词块
