## Why

当前覆盖层只在每个词下方绘制下划线，用户必须悬停才能在弹窗里看到读音。对于漫画/视觉小说阅读器，直接在汉字上方一眼看到读音（振假名）是核心价值。读音在识别阶段已由本地 UniDic 同步解析完成，展示它不需要额外成本。

## What Changes

- `WpfOverlayRenderer` 额外把每个词的读音以平假名形式、水平居中绘制在词外接框正上方，仅针对表面文本包含至少一个汉字（CJK 表意字符）的词。
- 振假名字号 = OCR 文字高度（词框高度）的 1/3，以 DIP 计。
- 读音取 `GroupedWord.Reading` 经 `Kana.ToHiragana` 转为平假名（整词读音居中，非逐字）。
- 不含汉字的词（纯假名/片假名/其他）不显示振假名。
- 振假名是静态的（不随悬停变化）；现有下划线与悬停行为不变。
- 识别时序不变——渲染会话时读音已同步可用，振假名与下划线同时出现。

## Capabilities

### New Capabilities
- `furigana-overlay`: 在覆盖层中把平假名读音显示在汉字上方。

### Modified Capabilities
- `window-word-overlay`: `Word underline overlay` 需求新增振假名渲染行为（在下划线之外叠加，不取代）。

## Impact

- `src/KotobaSenpai.Platform.Windows/Overlay/WpfOverlayRenderer.cs` — 在覆盖层画布中新增振假名文本元素。
- Core 模型不变：`GroupedWord.Reading`、`GroupedWord.Rects`、`Kana.ToHiragana` 均已存在。
- 无新增依赖。OCR、分词、会话流程均不改动。