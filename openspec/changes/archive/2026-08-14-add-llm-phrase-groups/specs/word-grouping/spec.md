## MODIFIED Requirements

### Requirement: Group OCR characters into tokenizer words
系统 SHALL 将 OCR 识别出的字符（按阅读顺序、含字符框）组合为句级 token 序列，并为每个 token 分配包含源 OCR 行身份的稳定分析 ID。每个 token 及每个连续本地词块 SHALL 映射回其成员字符框；跨越被本地句段划分判定为连续的 OCR 行时，系统 SHALL 保留跨行 token/词块关系，而不是把每行 offset 当作全局身份。

#### Scenario: Group a line of characters into tokens
- **WHEN** 某一行的 OCR 字符文本经分词器切分后得到若干 token
- **THEN** 系统为每个 token 计算一个词，词的包围盒为其全部成员字符框的并集

#### Scenario: Token spans map back to character boxes
- **WHEN** token 在行文本中的起始偏移与长度已知
- **THEN** 系统把组成该 token 的字符框聚合为一个词，保证词框覆盖所有成员字符

#### Scenario: Group lines into sentence tokens
- **WHEN** 相邻 OCR 行的阅读顺序、布局间距和标点边界表明它们属于同一文本段
- **THEN** 系统按句级顺序分词，并为 token 生成唯一的行感知 ID

#### Scenario: Map a token across an OCR line break
- **WHEN** 一个连续词的字符跨越两个被合并的 OCR 行
- **THEN** 系统使用两行对应字符框计算该词的多个行内几何，且保留同一个 token/span 身份

#### Scenario: Preserve independent line fallback
- **WHEN** OCR 行之间存在明显段落间距、上一行有句末标点或阅读顺序不可靠
- **THEN** 系统切分句段；每个句段仍独立生成本地 token 和词块，不跨段合并

#### Scenario: Word with no characters
- **WHEN** 一个 token 没有映射到任何有效字符框
- **THEN** 系统跳过该 token，不生成词或横线

## ADDED Requirements

### Requirement: Build multi-part phrase geometry
系统 SHALL 根据已验证的 phrase group token 引用生成一个或多个 phrase part 几何。每个 part SHALL 只覆盖其引用 token 的字符框；group 之间的间隔 token 不得被并入 phrase 几何。

#### Scenario: Build geometry for separated parts
- **WHEN** group 包含两个被其他 token 分隔的连续 parts
- **THEN** 系统为两个 parts 分别计算几何，并保留同一个应用 group ID

#### Scenario: Build geometry for a part crossing lines
- **WHEN** 一个 part 跨越两个相邻 OCR 行且属于同一句段
- **THEN** 系统为该 part 生成按行拆分的可绘制几何，而不是生成跨越空白区域的单一矩形
