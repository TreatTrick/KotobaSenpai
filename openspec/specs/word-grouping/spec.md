# word-grouping Specification

## Purpose
TBD - created by archiving change group-ocr-words-by-tokenizer. Update Purpose after archive.
## Requirements

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
系统 SHALL 在 UniDic 输出的完整 token 边界上生成连续候选词块，不得从 token 中间开始或结束；候选 SHALL 在一次识别内批量查询词典，并按从左到右的最长覆盖选择非重叠 span。对于包含至少一个汉字的多 token 候选，除精确拼接各 token `Surface` 外，当末尾 token 为可活用动词或形容词且其 `Surface` 与辞书形（`OrthBase`/`Lemma`）不同时，系统 SHALL 额外生成“前缀 `Surface` + 末尾 token 辞书形”作为词典候选；命中后 SHALL 按既有活用延续规则吸收紧随其后的助动词及接续助词 `て`/`で`，使单个 span 覆盖完整出现形，并以命中的辞书形作为 `LookupKey`。纯假名多 token 序列 SHALL 不触发末尾辞书形归一化匹配，以避免误合并普通助词与活用短串。

对于紧邻汉字词元的可规范化阿拉伯数字片段（半角 `0-9` 或全角 `０-９` 构成的无前导零的 1–99 整数），系统 SHALL 额外生成将数字片段规范化为汉数字的词典查词候选（例如为 `1週間` 生成 `一週間` 查词键），并在命中唯一词典读音时保留原始 OCR `Surface`（`1週間`）同时采用命中的有效读音（`いっしゅうかん`）。当连续 token 序列未在 JMdict 命中整词，但严格匹配 `[合法前部（1–99 汉数字/半角/全角数字或「何」；「曜日」对应「月/火/水/木/金/土/日/何」）] + [首批白名单助数词（週間/時間/年生/ヶ月/か月/ヵ月/カ月/箇月/曜日）]` 时，系统 SHALL 将其合并为单个连续 span，并依据标准音变表（含促音化及 `よ/く` 特殊读音）推导其有效读音（如 `三週間` → `さんしゅうかん`、`四時間` → `よじかん`、`六ヶ月` → `ろっかげつ`）；超出可确定范围的数字或非白名单助数词 SHALL 不强行合并或编造读音。

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

#### Scenario: 半角数字加助数词通过数字规范化命中词典
- **WHEN** token 序列为 `1`、`週間`，JMdict 收录 `一週間`（唯一读音 `いっしゅうかん`）而未收录 `1週間`
- **THEN** 系统生成一个覆盖 `1週間` 的 span，保留 `Surface = "1週間"`，并将有效读音设为 `いっしゅうかん`

#### Scenario: JMdict 缺整词条目时合并白名单助数词并推导有效读音
- **WHEN** token 序列为 `三`、`週間`（或 `3`、`ヶ月`），且 JMdict 中没有对应整词记录
- **THEN** 系统将其合并为单个 span `三週間`（或 `3ヶ月`），并推导出有效读音 `さんしゅうかん`（或 `さんかげつ`）

#### Scenario: 数词续接位置仍参与普通词典匹配
- **WHEN** token 序列为 `百`、`一`、`度`，且 JMdict 仅命中 `一度`
- **THEN** `一度` 仍参与普通复合词匹配。从数词续接位置开始的候选不使用数字归一化或白名单助数词推导

### Requirement: Build multi-part phrase geometry
系统 SHALL 根据已验证的 phrase group token 引用生成一个或多个 phrase part 几何。每个 part SHALL 只覆盖其引用 token 的字符框；group 之间的间隔 token 不得被并入 phrase 几何。

#### Scenario: Build geometry for separated parts
- **WHEN** group 包含两个被其他 token 分隔的连续 parts
- **THEN** 系统为两个 parts 分别计算几何，并保留同一个应用 group ID

#### Scenario: Build geometry for a part crossing lines
- **WHEN** 一个 part 跨越两个相邻 OCR 行且属于同一句段
- **THEN** 系统为该 part 生成按行拆分的可绘制几何，而不是生成跨越空白区域的单一矩形

### Requirement: Group words across merged sentence lines
系统 SHALL 将同一句段(按句末标点 / 大段落间距断开的连续行块)的多行文本拼成一个文本块,对该文本块一次性分词,并把每个 token 的 offset 分段映射回原始各行,为一个 token 生成一个 `GroupedWord`,携带**每行一个 rect** 的多 rect 几何。跨行词(其字符分布于多行)因此成为一个 `GroupedWord` 而非多个残片。单行词 SHALL 仅含一个 rect。

#### Scenario: A word split across two lines becomes one GroupedWord
- **WHEN** 一个词(如 世界)的字符分布于两行(`せ` 在行1、`かい` 在行2),两行属于同一句段
- **THEN** 分词器在合并文本上把它识别为一个 token,生成一个 `GroupedWord`,其 `Rects` 含两个 rect(分别对应行1 的 `せ` 框与行2 的 `かい` 框)

#### Scenario: A single-line word keeps one rect
- **WHEN** 一个词完整位于同一行
- **THEN** 该 `GroupedWord` 的 `Rects` 仅含一个 rect(该行字符框并集)

#### Scenario: Lines in different segments do not merge
- **WHEN** 两行被句末标点或大段落间距分割到不同句段
- **THEN** 系统不跨段合并 token,各自独立分词

#### Scenario: Token offset maps back to the correct line's boxes
- **WHEN** 一个 token 的 offset 落在合并文本的某一行区间内
- **THEN** 系统从该行取字符框,不越界到其他行
