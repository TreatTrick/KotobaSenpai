## ADDED Requirements

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