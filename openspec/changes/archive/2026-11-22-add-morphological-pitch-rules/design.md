## Context

Phase 1（`add-dictionary-pitch-correction`）已建立整词音调模型 `SpanPitch`、统一协调门面 `WordPitchResolver`、覆盖层跨行着色（`OverlayAnnotationLayer` + `FuriganaLayout.GetSpanParts`）以及弹窗全候选展示（`PhrasePopupViewModel`）。当前分层离线音调解析管线为：

`L1 ManualPitchOverrides` → `L2 Kanjium SQLite (pitch-accent.db)` → `L3 WhitelistedCounterPitchRules` → 源 token UniDic `aType` / 单 token `WordPitch` 降级。

但 Kanjium 只收录静态词典形，导致两类高频场景仍缺失准确的整词音调：
1. **单 token 活用变形词**：如 `食べよう`、`行こう`，UniDic 给出的 `aType` 是基本形（`2` / `0`），实际出现形需按 `aModType`（`M1@1`）变形为 `たべよ＼う [3]` / `いこ＼う [2]`。
2. **多 token 活用链与词典缺项复合词**：`TokenBoundarySpanResolver` 已经将 `食べて`、`食べなかった`、`食べます`、`食べませんでした`、`食べさせられた`、`高かった`、`赤かった`、`高くて`、`赤くて`、`行ったら`、`食べすぎる`、`食べやすい`、`論理的`、`物理学` 等合并为单个 `GroupedWord`，但由于当前只读取源 token 独立的 `aType`，覆盖层呈现割裂的原形音调而非实际连读变调结果。

项目随包分发的 UniDic 3.1.0（`LibNMeCab` `MeCabUniDic22Node`）已经原生包含 `AConType`（アクセント結合型）与 `AModType`（アクセント修飾型），无需新增任何词典资产或外部依赖即可实现纯离线、微秒级的形态学音调计算。

## Goals / Non-Goals

**Goals:**
- 在 `Token` 与 `UniDicTokenizer` 中无破坏性地透传 `AConType` 与 `AModType` 原始字段。
- 在 `KotobaSenpai.Core` 实现纯 C# 的形态学音调引擎 `MorphologicalPitchRules`，支持 UniDic 标准的 `AModType`（`M1@m`、`M2@m`、`M4@m`，含特殊拍与无声化母音保护）和 `AConType`（付属語 `F1`–`F6`、複合語/接尾辞 `C1`–`C5`、确定性接頭辞 `P` 规则）。
- 将其作为 L4 接入 `SpanPitchResolver`（`ManualOverride → Dictionary → CounterRule → MorphologicalRule`），仅作用于未发生读音覆盖（`!word.HasOverriddenReading`）的词块，直接复用 Phase 1 的 `SpanPitch` 跨行覆盖层着色与弹窗展示。
- 严格遵循失败关闭（Fail-Closed）原则：缺少必需字段、命中未支持规则标记或核位越界时返回 `null`，回落既有行为而不伪造音调。
- 以 k3zi Gist 核对后的动词/形容词活用表与常见接辞例词建立黄金单元测试集，并补充基于本地 UniDic 3.1.0 的端到端集成测试。

**Non-Goals:**
- 不修改 `TokenBoundarySpanResolver` 的词块合并边界或下划线范围（例如不扩展 `ば`/`たり` 合并或跨词块句法合并）。
- 不实现跨 `GroupedWord` 的句法连读变调（如 `尾高 + の` 平板化、`〜の上/下/人`、副词化变调，留待 Phase 3 评估）。
- 不修改 WPF 覆盖层布局、弹窗 XAML 模板、LLM prompt 契约或安装包资产。

## Decisions

### 1. Core 切片分层放置（遵守 `core-slice-architecture`）
- **决策**：
  - `SpanPitchSource.MorphologicalRule` 新增至 `Core.Japanese/SpanPitch.cs`（Layer 0）。
  - 纯拍级音韵辅助函数（特殊拍 `っ/ん/ー` 判定、无声化母音判定）放在 `Core.Japanese/PitchAccent.cs`（Layer 0，仅操作拍字符串，零跨层依赖）。
  - `Token.AConType` 与 `Token.AModType` 放在 `Core.Tokenization/Token.cs`（Layer 3，在现有构造函数末尾追加默认空字符串参数，零破坏现有调用方）。
  - `MorphologicalPitchRules` 放在 `Core.WordGrouping/MorphologicalPitchRules.cs`（Layer 5，与其唯一消费者 `SpanPitchResolver` 同切片，合法向下依赖 Layer 3 `Token` 与 Layer 0 `PitchAccent`）。
- **备选方案**：曾考虑将 `MorphologicalPitchRules` 放入 `Core.Japanese`（Layer 0），但 Layer 0 不得依赖 Layer 3 `Token`，需要额外定义重复的 DTO 镜像，得不偿失；放入 `Core.WordGrouping` 既零适配器代码又严格符合分层架构测试。

### 2. 分层解析优先级与适用前置条件
- **决策**：`SpanPitchResolver` 解析顺序升级为：
  1. **L1 `ManualPitchOverrides`**（人工校正最高优先）
  2. **L2 `IPitchAccentDictionary`**（Kanjium SQLite 精确 `(Surface, Reading)` 匹配，确保静态词典形与惯用复合词如 `お寿司`、`論理的` 优先取权威收录值）
  3. **L3 `WhitelistedCounterPitchRules`**（首批 5 类助数词白名单，覆盖含数字变体与音变推导）
  4. **L4 `MorphologicalPitchRules`**（仅当 `!word.HasOverriddenReading` 时执行）：
     - 对单 token 词块：仅当 `AModType` 非空且可解析（如 `食べよう` `M1@1`、`行こう` `M1@1`、`赤かっ` `M2@2`）时计算变形后 `SpanPitch`；无 `AModType` 的普通单 token 仍返回 `null` 交由既有 UniDic `aType` 路径处理。
     - 对多 token 词块（`SourceTokens.Count > 1`）：从左至右逐语素递推计算整词音调核。
- **为什么禁止在 `HasOverriddenReading == true` 时运行 L4**：一旦读音被 JMdict 或 LLM 改写，源 UniDic token 的 `Kana`/`AType`/`AModType` 与最终有效读音已不再对齐，套用源 token 结合规则会产生串调；此类词块仍由 L1–L3 整词查表或单 token `WordPitch` 参考处理。

### 3. UniDic `AModType` / `AConType` 递推状态机规则（经本地 UniDic 3.1.0 实测验证）
状态为 `(Morae, N, M, Category)`，其中 `N = Morae.Count`、`M` 为当前音调核（`0` 表示平板）、`Category ∈ { 動詞, 形容詞, 形状詞, 名詞, 副詞 }`。

#### 3.1 活用变形规则 `AModType`（单 token 初始化与每次接续后应用）
| 标记 | 公式 | 实测案例 |
|---|---|---|
| `M1@k` | `N - k`（无条件生效，需满足 `1 <= N - k <= N`） | `食べよう (N=4, aType=2, M1@1)` → `3`（`たべよ＼う`）；`行こう (N=3, aType=0, M1@1)` → `2`（`いこ＼う`） |
| `M2@k` | `M == 0 ? N - k : M` | `行かなかった` 中 `なかっ (N=5, M=0, M2@2)` → `3`（`いかな＼かった`）；`食べなかった` 中 `なかっ (N=5, M=2, M2@2)` → `2`（`たべ＼なかった`）；`赤かっ (N=4, M=0, M2@2)` → `2` |
| `M4@k` | `M == 0 → 0`；`M == 1 → 1`；否则候选 `shifted = Math.Max(1, M - k)`，并应用下述音韵移位保护 | `食べ (N=2, aType=2, M4@1)` → `1`（`た＼べて`）；`見 (N=1, aType=1, M4@1)` → `1`（`み＼て`）；`集め (N=3, aType=3, M4@1)` → `2`（`あつ＼めて`） |

- **`M4@k` 的音韵移位保护**（对齐东京式标准与 k3zi Gist 例外）：
  1. 若目标拍 `shifted`（1-based）为**无声化母音**（`k/s/t/h/p` 行 `i/u` 段假名，且后接 `k/s/t/h/p` 行清辅音，例如 `つけ` 的 `つ`、`たすけ` 的 `す`）：核不前移，保持原 `M`（`つけ＼て [2]`、`たすけ＼て [3]`）。
  2. 否则若目标拍 `shifted` 为**特殊拍**（`っ`、`ん`、`ー`）且 `shifted > 1`：继续左移一拍至 `shifted - 1`（例如 `ぞんじ＼る` → `ぞ＼んじて [1]`）。

#### 3.2 付属語结合规则 `AConType`（`Pos1 ∈ { 助動詞, 助詞 }`）
从 `t.AConType` 逗号分隔条目（如 `動詞%F2@1,形容詞%F4@-2`）中按当前 `Category` 查找对应 `F` 规则（未命中或格式非法则返回 `null`）：
| 标记 | 公式 | 说明与实测案例 |
|---|---|---|
| `F1` | `M = M1` | 従属型：`食べて [1]`、`書いて [1]`、`歩いて [2]`、`呼んで [0]`、`飲んで [1]` |
| `F2@v` | `M = (M1 == 0) ? (N1 + v) : M1` | 不完全支配型：`高くて [2]`、`赤くて [2]`、`行ったら [3]`、`食べたら [1]`；**动词/助动词末拍平板归一**：当 `M1 == 0` 且 `t` 为 `助動詞-タ`（`終止形`/`連体形` 单拍 `た`/`だ`）使 `N1 + v == N1 + N2` 时，归零为平板 `0`（`行った [0]`、`呼んだ [0]`），避免生成不存在的动词尾高型 |
| `F3@v` | `M = (M1 == 0) ? 0 : (N1 + v)` | 融合型：`食べない [2]`、`行かない [0]`、`食べられる [4]`、`食べさせられる [6]`、`食べさせられた [5]` |
| `F4@v` | `M = N1 + v` | 支配型：`食べます [3]`、`行きます [3]`、`食べません [4]`、`食べませんでした [4]`、`食べたい [3]`、`高かった [2]`、`赤かった [2]` |
| `F5` | `M = 0` | 平板化型 |
| `F6@v1,v2` | `M = (M1 == 0) ? (N1 + v1) : (v2 == -1 ? M1 : N1 + v2)` | 复合型：`行ったり [3]`、`食べたり [1]`、`歩いたり [2]` |

- **Category 继承关键决策**：经过 UniDic 3.1.0 实测验证，动词后接 `助動詞`（包括 `助動詞-ナイ`、`助動詞-タイ`、`助動詞-マス`、`助動詞-レル`）时，主类别 `Category` **保持为 `動詞`**（而非切换为 `形容詞`），确保 `食べなかった`（`食べ + なかっ + た`）后续 `た` 命中 `動詞%F2@1` 得到 `[2]`（`たべ＼なかった`），`行かなかった` 经 `なかっ` 的 `M2@2` 得到 `[3]` 后由 `動詞%F2@1` 保持 `[3]`（`いかな＼かった`）。

#### 3.3 接尾辞 / 複合語结合规则 `C1`–`C5` 与 接頭辞 `P` 规则
- **接尾辞 / 后部要素（`Pos1 == "接尾辞"` 或 `AConType ∈ { C1..C5 }`）**：
  - `C1`（保存型）：需 `t.PitchAccentPosition` 有效且 `> 0`，`M = N1 + M2`（如 `食べ + やすい(2, C1)` → `4` `たべやす＼い`；`食べ + すぎる(2, C1)` → `4` `たべすぎ＼る`）。
  - `C2`（生起型）：`M = N1 + 1`。
  - `C3`（前部末型）：`M = N1`（若第 `N1` 拍为特殊拍 `っ/ん/ー` 且 `N1 > 1` 则左移为 `N1 - 1`；如 `物理(3) + 学(C3)` → `3` `ぶつり＼がく`）。
  - `C4`（平板化型）：`M = 0`（如 `論理 + 的(C4)` → `0` `ろんりてき`）。
  - `C5`（前部保存型）：`M = M1`。
  - 结合后 `Category` 更新为后部要素的类别（例如 `食べすぎる` 保持 `動詞`、`食べやすい` 变为 `形容詞`，使其后接 `た` 时能继续推导出 `食べすぎた [3]`、`食べやすかった [4]`）。
- **接頭辞（`tokens[0].Pos1 == "接頭辞"` 且后部要素有明确 `AType`）**：
  - 仅支持高确定性 `P` 标记（`P1`：`M2 == 0 || M2 == N2 ? 0 : N1 + M2`；`P2`：`M2 == 0 ? 0 : N1 + M2`；`P6`：`0`；`P13`：`M1`；`P14`：`M2 == 0 ? 0 : N1 + M2`），遇到歧义标记（如 `P4`）或无 `M2` 时返回 `null` 失败关闭（绝大多数高频 `お〜`/`ご〜` 词仍由 L2 Kanjium 优先命中）。

## Risks / Trade-offs

- **[Risk] UniDic 中部分 `aConType` 包含未覆盖的稀有组合格式（如多候选 `C1,C2` 或非常规词性前缀）** → **Mitigation**：严格解析单值或匹配当前 `Category` 的已知标记；凡无法唯一确定或未实现的标记一律返回 `null`（Fail-Closed），安全回落到既有单 token 渲染，绝不输出错误红蓝着色。
- **[Risk] `M4@k` 无声化母音判定过于激进可能误抑制正常核前移** → **Mitigation**：仅匹配严格标准清音条件（`k/s/t/h/p` 行 `i/u` 段假名后接 `k/s/t/h/p` 行清辅音），并用 `食べて`/`集めて`/`つけて`/`助けて`/`ぞんじて` 等正反例覆盖单元测试。
- **[Risk] 形容词传统音调与现代新音调并存（如 `たか＼かった [2]` vs `た＼かかった [1]`）** → **Mitigation**：UniDic 规则输出标准现代音调（NHK 新辞典主流，如 `たか＼かった [2]`、`おおき＼かった [3]`），与覆盖层单主核渲染模型一致；若未来需要并列传统音调变体，可在弹窗层独立扩展，不影响当前核心模型。
- **[Risk] 单 token 词块（如 `食べ` 单独出现作为连用形）在 Kanjium 无条目时会命中 `M4@1`** → **Mitigation**：在日语中动词连用形作 `食べに行く` 时实际读法确实是 `た＼べ [1]`（Gist 第 280 行），而名词化形式（如 `動き`）在 UniDic 中 `Pos1 == "名詞"` 且 `AModType == "*"`，不会被误触发。

## Migration Plan

纯本地代码演进，无新增数据文件、无配置迁移、无破坏性公开 API 变更。若需回滚，仅需在 `SpanPitchResolver` 中移除 L4 调用即可完全恢复 Phase 1 行为。
