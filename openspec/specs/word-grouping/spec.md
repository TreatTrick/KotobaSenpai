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

