## Why

目前 OCR 是字符级识别，覆盖层对**每个识别字符**画一条下划线，视觉上碎成一个个单字，无法体现"词"的边界。分词器（UniDic/MeCab）已实现并注册在 DI 中但尚未接入取词/渲染流程。需要引入一个机制，把 OCR 识别出的字符框经分词器重新组合成词，每个词只画一条下划线，并在悬停时让该词下方整条线变色。

## What Changes

- 新增一个**词分组机制**（Core 服务）：把一行 OCR 字符文本经 `ITokenizer` 切分，用 token 的 span 映射回字符框，为每个 token 计算一个并集包围盒（一个词 = 一条线）。
- **修改**覆盖层渲染：从"每字符一条线"改为"每个分词 token 一条线"。分词范围 = 全部词含助词，排除标点与空白。
- **新增悬停交互**：鼠标悬停在词的整块包围盒上时，该词下方整条线由默认天蓝（`DeepSkyBlue`）变为橙红（`#FF4500`）；其余词的线保持默认色。悬停仅变色，不弹释义/气泡。
- 覆盖层窗口保持整体点击穿透（不拦截点击），但在词热区上捕获悬停以触发变色。

## Capabilities

### New Capabilities
- `word-grouping`: 将 OCR 字符级识别结果经分词器重新组合成"词"，并计算每个词的下划线几何（并集包围盒）的机制。

### Modified Capabilities
- `window-word-overlay`: "Word underline overlay" 需求将变为"每个分词词一条下划线"，并新增悬停变色行为。

## Impact

- **Core**：新增 `word-grouping` 服务；`WordOverlaySession` 从每字符一条 `OverlayLine` 改为每 token 一条；`Token` 已有 `StartOffset`（需补算 token 长度以映射 span）。
- **Platform.Windows**：`WpfOverlayRenderer` 新增词热区命中测试 + 悬停变色（保持整窗点击穿透）；`MeikiOcrWordRecognizer` 输出需保留字符顺序与文本以支持分组。
- **App**：DI 注册分组服务；`MainWindowViewModel` 识别流程串联分组逻辑。
- **依赖**：复用已有 `ITokenizer`（`japanese-tokenizer`），无新增外部依赖。