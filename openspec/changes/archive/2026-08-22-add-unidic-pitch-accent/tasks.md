## 1. Core 音调模型与计算

- [x] 1.1 新增 Core 音调纯逻辑模块：解析原始 `aType` 的第一个合法候选，区分未知值，并按小假名、`っ`、`ん`、长音规则拆分读音为拍。
- [x] 1.2 实现 `0`/`1`/`N` 音调核到高低拍模式和统一 notation（如 `[2] LH↓L`）的转换，覆盖平板、头高、中高、尾高、单拍和越界回退。
- [x] 1.3 扩展 `Token`，保留现有原始 `AType` 并暴露可空规范化音调核；保持现有 `Token` 构造调用和无音调测试替身兼容。
- [x] 1.4 为音调解析、拍拆分、模式生成、notation 和未知回退添加 Core 单元测试。

## 2. UniDic 与本地传播链路

- [x] 2.1 更新 `UniDicTokenizer` 相关测试，验证单值、多值/带引号 CSV、`*`、非法值和词典字段对齐；确认 tokenizer 仍只使用 UniDic，不引入 Kanjium 或其他外部音调源。
- [x] 2.2 更新 `LookupSpan`/`GroupedWord` 的音调片段传播，使合并词保留每个源 UniDic token 的读音、音调核和 surface/reading 偏移，而不是只依赖显示 token 的第一个 `AType`。
- [x] 2.3 扩展 `LocalSpanSummary`、`WordMeaningView`、phrase part/view 和 `SentenceTokenBuilder`，携带按源 token 顺序排列的本地音调片段及统一 notation。
- [x] 2.4 更新 `WordMeaningValidator`、`WordOverlaySession` 和 phrase geometry 映射，确保有无 LLM 词义时都能从本地词数据恢复读音和音调，且未知片段不阻断其他字段。
- [x] 2.5 添加 Core 测试，验证跨多个 UniDic token 的合并词、重复 surface、跨句段 token 引用和缺失音调的传播行为。

## 3. 覆盖层音调绘制

- [x] 3.1 在 `WpfOverlayRenderer` 中复用 Core 拍模式，按拍将汉字振假名绘制为高音红色 `#FF4444`、低音蓝色 `#4488FF`，保留现有黑色描边、字号、DPI 和顶部裁剪。
- [x] 3.2 为纯平假名、片假名和汉字词送假名绘制按拍红/蓝小点；使用完整读音模式和片段偏移，避免小假名拆分错误、送假名错位和重复计数。
- [x] 3.3 为平板型保留 DokiDokiDict 风格的尾部辅助标记；音调未知时回退到单色振假名或不画点，不把未知值当作低音。
- [x] 3.4 保持音调文字/小点静态、点击穿透、悬停下划线高亮和覆盖层刷新清理行为；覆盖层不增加新的命中区域。
- [x] 3.5 增加 Windows overlay/geometry 测试，覆盖汉字按拍颜色、纯假名点数、送假名偏移、无音调回退、跨行词、DPI 映射和刷新无残留。

## 4. 句子与词释义展示

- [x] 4.1 扩展 `PhrasePopup` 的词条渲染，在词头、词性和振假名读音旁显示本地音调 notation；未知音调显示本地化回退，不隐藏释义。
- [x] 4.2 更新 phrase group 详情，使组内每个本地合并词都能显示读音和音调；LLM 未返回该词或整个分析失败时使用本地词条回退。
- [x] 4.3 增加/更新仓库支持的英文、中文资源键和 popup 数据回退测试，验证成功分析、等待、失败、离线、无音调和合并词多片段场景；日文界面沿用英文 neutral fallback。
- [x] 4.4 验证 `PhrasePromptBuilder`、各 provider serializer 和 parser 的 payload/契约未新增 pitch 字段，音调只在本地结果映射和展示边界使用。

## 5. 验证与交付

- [x] 5.1 运行格式化、Core/App/Platform.Windows 重点测试和完整 solution 测试，修复由模型字段扩展引起的构造器或测试替身回归；全量测试通过，格式校验仅报告既有无关文件的空白问题。
- [x] 5.2 运行 `openspec validate --change "add-unidic-pitch-accent"` 和 `openspec status --change "add-unidic-pitch-accent"`，确认所有工件完成并可进入 apply 阶段。
