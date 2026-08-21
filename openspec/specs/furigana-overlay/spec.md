# furigana-overlay Specification

## Purpose
TBD - created by archiving change add-furigana-overlay. Update Purpose after archive.
## Requirements
### Requirement: 汉字词上方显示振假名
系统 SHALL 对每个表面文本包含至少一个 CJK 表意字符的本地分词词，把其合并读音转为平假名，水平居中绘制在词外接框正上方。读音取词的合并 UniDic 读音转平假名。不含汉字的词（纯平假名/片假名/其他文字）SHALL 不显示振假名。振假名字号 SHALL 默认取词 OCR 文字高度（词外接框高度）的 1/3，以 DIP 计，且 SHALL 可通过设置项 `FuriganaFontScale`（比例值）配置；设置缺失或非法时 SHALL 回退到默认 1/3。振假名 SHALL 为静态：不随悬停变化，不影响命中测试，也不破坏点击穿透。

#### Scenario: 为汉字词显示振假名
- **WHEN** 会话包含一个表面有汉字且读音非空的词
- **THEN** 覆盖层把该读音（转平假名）居中绘制在词外接框正上方，字号为外接框高度 × 配置比例（默认 1/3）

#### Scenario: 按设置调整振假名字号
- **WHEN** 设置项 `FuriganaFontScale` 为一个合法比例值（如 0.25）
- **THEN** 覆盖层以该比例计算振假名字号，而非固定 1/3

#### Scenario: 设置缺失或非法时回退默认
- **WHEN** `FuriganaFontScale` 未设置、为空或不是合法数值
- **THEN** 覆盖层回退到默认比例 1/3，不报错

#### Scenario: 跳过不含汉字的词
- **WHEN** 词的表面不含汉字（纯平假名、片假名或其他文字）
- **THEN** 覆盖层在该词上方不绘制任何振假名

#### Scenario: 悬停时振假名保持静态
- **WHEN** 光标移动到显示振假名的词上
- **THEN** 振假名保持不变（下划线颜色可变化），且覆盖层仍为点击穿透

