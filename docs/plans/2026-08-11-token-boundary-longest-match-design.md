# UniDic 边界约束最长匹配设计

## 背景

UniDic 输出的是形态素（例如 `で`、`も`），而悬停查词需要的是词典词块（例如 `でも`）。当前系统直接把每个形态素画成一个词，导致悬停从 `も` 开始时可能得到错误的 `もち`。DokiDokiDict 的字符级最长匹配可以命中更长词，但会从任意字符起点开始，因而会跨越 UniDic 已知边界。

## 目标与非目标

目标：

- 只在完整 UniDic token 边界上生成候选词块。
- 对连续候选做从左到右、非重叠的最长匹配。
- 直接词典词（`でも`、`そしたら`、`オールラウンダー`）和 UniDic lemma 活用链（`なかった`、`考えてる`）都能生成一个 span。
- 下划线热区、悬停查词和诊断输出使用同一个最终 span。
- 识别期间用批量词典查询，避免每个候选打开一次 SQLite 连接；不复制图像或 ONNX tensor。

非目标：

- 本次不把相邻视觉行拼成段落；跨行词需要多矩形 span，另行处理。
- 不改变 OCR、UniDic 词典或 MeCab 版本。
- 不引入基于上下文的语法消歧；因此 `実戦でも` 按用户要求合并为 `でも`。

## 数据流

```text
OCR line
  -> UniDic Token[]
  -> token-boundary candidate enumeration
  -> one batched JMdict lookup
  -> greedy non-overlapping LookupSpan[]
  -> GroupedWord (span + geometry + entries)
  -> underline and hover popup
```

## 匹配规则

1. 标点、空白和 token 之间的非连续间隙是硬边界。
2. 直接候选由相邻完整 token 的 `Surface` 拼接产生；候选不能从 token 中间开始或结束。
3. 单 token 的词典键按 `Lemma -> OrthBase -> Reading -> BaseReading` 顺序选择。
4. 动词/形容词/助动词后面连续的活用助动词（以及明确的接续助词 `て/で`）可附着到基础 token；查词键仍使用基础 token 的 lemma。
5. 在同一 token 起点，优先最长字符 span；长度相同优先直接 surface 命中，再优先 lemma 活用命中。
6. 选中一个 span 后跳过其覆盖的 token，继续处理下一个未消费 token；没有词典命中时仍保留原 token 的下划线。

## 性能与兼容性

- 候选仅是短字符串和 token/span 元数据；不产生帧像素的额外拷贝。
- `IDictionaryLookup` 增加批量表单查询，SQLite repository 在单次识别中复用一个查询连接并按参数上限分块。
- `GroupedWord` 保留现有 `Token` 访问方式，并附带已解析的 entries；旧的 token-only 测试仍可运行。

## 验证标准

- `で / も / ちゃんと` 解析为 `で`、`でも`、`ちゃんと`，绝不产生 `もち`。
- `そ / し / たら` 解析为 `そしたら`。
- `オール / ラウンダー` 解析为 `オールラウンダー`。
- `なかっ / た` 解析为 surface `なかった`、lookup key `無い`。
- `考え / てる` 解析为 surface `考えてる`、lookup key `考える`。
- 标点和无命中 token 的现有下划线行为不回归；全量 `dotnet test` 通过。
