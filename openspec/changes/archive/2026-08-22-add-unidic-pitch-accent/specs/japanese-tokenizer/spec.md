## MODIFIED Requirements

### Requirement: Tokenize Japanese text

系统 SHALL 将日语文本分词为 token 序列。每个 token SHALL 暴露 surface、UniDic lemma、书写基本形 `orthBase`、表面形读音 `kana`、基本形读音 `kanaBase`、发音 `pron`、四级词性、活用类型、活用形、原始 UniDic `aType`、可选的规范化音调核以及原始输入中的起始偏移。规范化音调核 SHALL 使用 `0` 表示平板型、`1` 表示头高型、`N` 表示第 N 拍后下降；原始值缺失或无效时 SHALL 为空。

#### Scenario: 分词简单句
- **WHEN** 调用方使用日语文本 `日本語の解析テストです。` 调用 tokenizer
- **THEN** 结果包含 surface 依次为 日本/語/の/解析/テスト/です/。
- **AND** 每个 token 都暴露指向未修改输入的非负起始偏移

#### Scenario: 分词结果保留词典和读音字段
- **WHEN** tokenizer 处理 `買った` 中的活用动词
- **THEN** surface 为 `買っ` 的 token 暴露 UniDic lemma `買う`
- **AND** 该 token 的书写基本形、表面形读音、基本形读音和发音作为独立字段存在

#### Scenario: 分词结果保留四级词性和活用字段
- **WHEN** tokenizer 处理包含形态学详情的 token
- **THEN** token 以稳定位置暴露 pos1、pos2、pos3 和 pos4
- **AND** token 暴露活用类型和活用形，后续字段不发生错位

#### Scenario: 分词结果暴露规范化音调
- **WHEN** tokenizer 处理 `aType` 为 `2` 这样的有效 UniDic token
- **THEN** token 暴露原始 `aType` `2` 和规范化音调核 `2`

#### Scenario: 保留多值音调字段
- **WHEN** UniDic 节点的 `aType` 由带引号的 CSV 或多个逗号分隔候选值表示
- **THEN** token 将完整解码后的 `aType` 作为一个原始字段暴露
- **AND** 规范化音调核使用第一个合法候选，同时 lemma、读音、词性和后续 UniDic 字段保持对齐

#### Scenario: 非法音调保持未知
- **WHEN** UniDic 节点的 `aType` 为空、为 `*`、格式错误或超出读音拍数
- **THEN** token 保留原始字段，但不暴露规范化音调核

#### Scenario: 原始音调不是最终音调结果
- **WHEN** token 暴露原始 UniDic `aType`
- **THEN** tokenizer 不得声称该值包含 Doki 人工覆盖、Kanjium 数据或最终音调选择规则

#### Scenario: 保留固定词典的分词结果
- **WHEN** tokenizer 使用固定的 `unidic-py` `3.1.0+2021-08-31` 资源处理 `アルミホイルを買った`
- **THEN** `アルミホイル` 被拆成两个短单元 `アルミ` 和 `ホイル`

#### Scenario: 空输入不抛异常
- **WHEN** 调用方传入 null、空字符串或仅包含空白的文本
- **THEN** tokenizer 返回空 token 列表且不抛异常
