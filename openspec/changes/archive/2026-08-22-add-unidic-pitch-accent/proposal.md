## Why

当前 UniDic token 已经保留原始 `aType`，但这份数据还没有被转换为可展示的音调信息，也没有贯穿本地合并词、覆盖层和释义弹窗。学习者只能看到振假名，无法直接知道每个拍的高低变化；纯假名和送假名则完全没有音调提示。DokiDokiDict 已验证了基于 UniDic `aType` 按拍拆分、用红蓝颜色和小点呈现的交互方式，本次将这套能力引入 KotobaSenpai。

## What Changes

- 将 UniDic `aType` 解析为可供本地 UI 使用的音调核位置：`0` 表示平板型，`1` 表示头高型，`N` 表示第 N 拍后下降；无效、缺失或无法确定时保留“未知”状态。
- 在 token、合并词和句段词引用之间保留音调及其完整读音/拍边界，避免合并多个 UniDic token 时只保留第一个 token 的音调。
- 按 DokiDokiDict 的方式绘制音调：汉字上方的平假名按拍使用红色（高）和蓝色（低），假名和送假名上方按拍绘制红蓝小点；没有有效音调时回退为现有的单色振假名或不显示标记。
- 在句子/短语释义详情和词释义详情中，除振假名读音外显示可读的音调表示，并在没有音调数据时使用明确的未知/省略回退，不影响释义显示。
- 音调计算和展示完全在本地完成，不修改现有 LLM 请求协议、LLM 返回字段、释义来源、覆盖层命中测试或点击穿透行为。

## Capabilities

### New Capabilities

- `pitch-accent-display`: 定义 UniDic 音调的规范化、传播、按拍高低模式计算，以及覆盖层和释义详情中的音调展示。

### Modified Capabilities

- `japanese-tokenizer`: token 除原始 `aType` 外需要提供稳定、可消费的规范化音调结果，并保持多值 `aType` 的兼容性。
- `furigana-overlay`: 汉字振假名增加红蓝按拍绘制，纯假名/送假名增加红蓝音调小点。
- `llm-word-meanings`: 词详情和短语组详情中的本地读音同时显示音调。

## Impact

- Core 模型和本地服务：`Token`、`LookupSpan`、`GroupedWord`、`SentenceTokenReference`、`LocalSpanSummary`、`WordMeaningView` 以及 `SentenceTokenBuilder`/词义校验链路需要携带音调数据。
- Windows 平台：`UniDicTokenizer` 增加音调解析，`WpfOverlayRenderer` 增加按拍文本和小点绘制，`PhrasePopup` 增加音调文本展示；需要保持横排、DPI、送假名裁剪和跨行词的现有布局规则。
- 测试：增加 UniDic `aType` 解析、拍拆分/高低模式、纯假名小点、送假名偏移、合并词传播、句子和词释义回退，以及现有 LLM payload 不变的测试。
- 不新增外部服务或字典依赖；不改变设置迁移、识别流程、短语分析并发和诊断文件契约。
