# add-furigana-overlay 设计

## Context

覆盖层（`WpfOverlayRenderer.OverlayWindow`）是一个透明、置顶、点击穿透的 WPF 工具窗口，定位在目标窗口上方。`Render(WordOverlaySession)` 清空画布并为每个词 rect 添加一条下划线 `Border`。每个 `GroupedWord` 已携带 `Reading`（合并的 UniDic 出现形读音）和 `Rects`（逐行物理像素矩形），且在 `Show` 被调用前已同步解析完成。`Kana.ToHiragana` 把片假名读音转为平假名。

## Goals / Non-Goals

**Goals:**
- 把词的读音以平假名形式、居中显示在汉字词正上方，字号为 OCR 文字大小的 1/3。
- 仅针对包含至少一个汉字的词。
- 与现有下划线/悬停行为共存；OCR、分词、会话模型均不改动。

**Non-Goals:**
- 逐字振假名对齐（把 にほんご 拆到 日本語 各字上）——明确排除；需要歧义的字内读音拆分启发式。
- 振假名的悬停交互。
- 重排或避开上一行文字的遮挡。
- phrase group 弹窗的振假名（悬停时已显示读音）。

## Decisions

- **整词读音、居中于外接框上方。** 词外接框是各 rect 的并集；读音字符串水平居中在 `rect.X + rect.Width/2`，置于 `rect.Y` 上方。备选方案：逐字对齐（排除——无逐字读音数据，拆分有歧义，如 日本→にほん 可拆 に/ほん 或 にほ/ん）、词后内联（排除——用户要求正上方）。
- **字号 = 词外接框高度 × 可配置比例，以 DIP 计。** 覆盖层已把物理像素除以 DPI `scale`；字号设为 `rect.Height / scale × scaleFactor`。比例存设置键 `FuriganaFontScale`（默认 `1/3`，即对"OCR 文字大小的 1/3"的字面解读，OCR 字形高度近似词外接框高度）。`WpfOverlayRenderer` 构造注入可选的 `ISettingsService`，每次 `Render` 时读取比例；缺失或解析失败回退 `1/3`。在字形上方留一个小间隙（如 2 DIP），避免贴着字。
- **配置入口：设置面板"外观"卡片加一个 Slider。** 比例范围 0.1–0.5，滑块读写同一 `FuriganaFontScale` 键。`MainWindow` 通过依赖属性注入 `ISettingsService`（与 `ThemeService` 的同模式），code-behind 在 `ValueChanged` 写入、窗口初始化时读取（默认 `1/3`）。改动立即写盘，下次识别时 renderer 读到新值生效。
- **按汉字过滤。** 仅当词表面包含至少一个 CJK 表意字符（正则 `\p{IsCJKUnifiedIdeographs}` 或汉字区段判断）时才显示振假名。纯假名/片假名/其他文字跳过。
- **读音 = `GroupedWord.Reading` → `Kana.ToHiragana`。** 即出现形假名；片假名读音（如外来语/人名）按既有约定归一化为平假名。
- **振假名是画布的 `TextBlock` 子元素**，在与下划线相同的循环里加入，因此"刷新时清空 `_canvas.Children`"的既有逻辑会一并清除它，无需额外清理。静态，悬停逻辑不动。
- **顶部裁剪。** 若振假名顶部要超出覆盖层窗口（`rect.Y < textHeight`），则夹紧到窗口内而非绘制到屏幕外。

## Risks / Trade-offs

- [振假名遮挡上一行 OCR 文字] → v1 接受；用户明确要求"正上方"放置。后续可向下偏移或当上方 rect 在一个文字高度内时跳过。
- [WPF FontSize 与字形高度不一致] → FontSize 是 em 框而非精确字形高度，1/3 外接框高度可能视觉上略大或略小。可接受；若真实内容观感偏差，可用一个常量系数微调。
- [宽词的合并读音可能很长] → 读音居中；异常宽的词其振假名可能宽于外接框。接受。

## Migration Plan

无迁移——纯增量渲染改动。回滚即还原渲染器改动，不涉及数据或配置。

## Open Questions

无阻塞项。字号比例已通过 `FuriganaFontScale` 设置暴露，用户可自行微调，无需硬编码系数。