## Why

Phase 1（`add-dictionary-pitch-correction`）已经打通了静态整词词典（Kanjium SQLite）、高频助数词白名单与整词跨行覆盖层着色，但视觉小说中出现频率最高的**动词/形容词活用形**（如 `食べて`、`食べなかった`、`食べます`、`食べませんでした`、`食べさせられた`、`高かった`、`食べよう`、`行こう`）以及词典未收录的**能产前后缀复合词**（如 `食べすぎる`、`食べやすい`、`論理的`、`物理学`）仍会落空或退化为源 token 原形音调，导致标注与角色实际发音不一致。

项目已合法随包分发的 UniDic 3.1.0 词典（`MeCabUniDic22Node`）原生自带日本国立国语研究所（NINJAL）编纂的 `AConType`（结合型）与 `AModType`（活用变形型）字段。透传这两个字段并在 `KotobaSenpai.Core` 实现纯离线的形态学音调结合规则引擎，可在零新增外部资产、零网络请求、零 LLM Token 成本的前提下，复用 Phase 1 的 `SpanPitch` 渲染与弹窗管线补齐动态活用与构词变调短板。

## What Changes

- **UniDic 分词元数据扩展**：在 `Token` 与 `UniDicTokenizer` 中透传 `MeCabUniDic22Node` 的 `AConType`（アクセント結合型）与 `AModType`（アクセント修飾型）原始字段（向后兼容现有构造签名，缺失或 `*` 时归一化为空字符串）。
- **新增 `SpanPitchSource.MorphologicalRule` 与纯离线形态学音调引擎**：在 `KotobaSenpai.Core/Japanese/` 实现纯逻辑的 `MorphologicalPitchRules`，在未发生读音覆盖的前提下，利用 `SourceTokens` 的 `AType`、`AModType`（`M1@m`、`M2@m`、`M4@m`，含特殊拍 `っ/ん/ー` 与无声化母音移位保护）和 `AConType`（付属語 `F1`–`F6`、複合語/接尾辞 `C1`–`C5`、接頭辞 `P1`/`P2`/`P4`/`P6`/`P13`/`P14`）逐语素递推计算整词音调核。
- **接入分层离线音调解析管线**：在 `SpanPitchResolver` 中将 `MorphologicalPitchRules` 接入为第 4 层（**`L1 ManualOverride` → `L2 Kanjium Dictionary` → `L3 WhitelistedCounterRule` → `L4 MorphologicalRule`**），覆盖单 token 带 `AModType` 的活用词（如 `食べよう [3]`、`行こう [2]`）、多 token 动词/形容词活用链（含多层叠链如 `食べさせられた [5]`、`食べませんでした [4]`）以及 Kanjium 缺项的能产复合/接辞词块。
- **严格失败关闭（Fail-Closed）与黄金测试集**：遇到读音被覆盖、缺少必需字段或未支持的规则标记时返回 `null` 回落既有路径，绝不伪造音调；以 k3zi Gist 核对后的动词/形容词活用表与常见接辞例词构建 `KotobaSenpai.Core.Tests` 黄金验收测试集，并在本地 UniDic 集成测试中验证端到端真实分词输出。
- **范围边界（Phase 2）**：不改变 `TokenBoundarySpanResolver` 的词块切分与下划线边界，不引入跨 `GroupedWord` 的句法连读（如 `尾高 + の` 平板化、副词化），不修改覆盖层/弹窗布局或 LLM 请求体。

## Capabilities

### New Capabilities
<!-- 本次变更在既有分词与音调展示能力上扩展，不引入新的顶层能力切片 -->

### Modified Capabilities
- `japanese-tokenizer`: 在 `Token` 与 `UniDicTokenizer` 中额外保留并暴露 UniDic 节点的原始 `AConType`（结合型）与 `AModType`（活用变形型）字段。
- `pitch-accent-display`: 在分层离线整词音调解析管线中新增 `MorphologicalRule`（第 4 层），基于 UniDic `AType`/`AModType`/`AConType` 计算未覆盖读音词块的活用与构词整词音调（含单 token 活用形变形、多层活用链与常见接辞复合，以及特殊拍/无声化母音保护与失败关闭回退）。

## Impact

- **Core (`KotobaSenpai.Core`)**：
  - `Japanese/SpanPitch.cs`：新增 `SpanPitchSource.MorphologicalRule`。
  - `Japanese/MorphologicalPitchRules.cs`（新增）：纯 C# 形态学音调结合/变形规则与特殊拍/无声化母音辅助判定（Layer 0，零外部依赖）。
  - `Tokenization/Token.cs`：新增 `AConType`、`AModType` 属性（兼容默认值）。
  - `WordGrouping/SpanPitchResolver.cs`：在 L1–L3 未命中且 `!word.HasOverriddenReading` 时调用 `MorphologicalPitchRules`。
- **Platform (`KotobaSenpai.Platform.Windows`)**：
  - `Japanese/UniDicTokenizer.cs`：透传 `node.AConType`、`node.AModType`。
- **Presentation (`KotobaSenpai.Presentation.Wpf`) / App / Packaging**：无结构性改动，直接复用 Phase 1 的 `SpanPitch` 覆盖层跨行着色、弹窗展示与诊断输出。
- **Tests (`tests/`)**：新增 `MorphologicalPitchRulesTests`（覆盖 `M1/M2/M4`、`F1`–`F6`、`C1`–`C5`、`P` 规则及 k3zi Gist 黄金活用用例）、扩展 `SpanPitchResolverTests` 与 `UniDicTokenizerTests` 真实分词端到端校验。
