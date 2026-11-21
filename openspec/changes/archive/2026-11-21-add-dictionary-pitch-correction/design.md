## Context

KotobaSenpai 当前的音调管线按源 UniDic token 逐个读取静态 `aType`，由 `GroupedWord.PitchAccents` 按 token 顺序传播至覆盖层（`OverlayAnnotationLayer`）和悬停弹窗（`PhrasePopupViewModel`）。在已有实现中存在四个明显缺口：

1. **短单位切分导致复合词音调割裂**：UniDic 将 `一週間`、`一時間` 等拆分为多个短单位 token，即使 JMdict 回流了整词读音 `いっしゅうかん`，旧规则也会将整组音调抑制为“未知”，覆盖层退化为单色振假名。
2. **数字表记变体缺失匹配**：本地 JMdict 同时收录 `一週間` 与 `１週間`，但视觉小说中常见的半角写法 `1週間` 原样查表为空，既无法触发 JMdict 读音回流，也无法命中整词音调。
3. **读音纠偏后缺少整词词典回查**：当 LLM 将 `額` 从 `がく` 纠偏为 `ひたい` 时，现有 `CorrectedWordPitch` 仅回查 UniDic 单词并标注为弹窗参考，覆盖层保持单色；而对于 `二週間`、`三週間` 等未收录进词典的高频数词＋助数词组合，系统完全无音调结果。
4. **覆盖层着色尚不支持整词跨行**：`FuriganaLayout.GetSpanParts` 目前仅服务于单色振假名，`AddColoredFurigana` 与 `AddPitchDots` 仍强绑定单个 `sourceTokenIndex`。

同时，项目已有成熟的离线 SQLite 资产打包范式（`tools/JmdictIndexBuilder` + `scripts/Prepare-Jmdict.ps1` + `KotobaSenpai.App.csproj` 发布校验 + `JmdictSqliteRepository` 按需只读查询），以及严格的本地音调红线：未知音调不得伪造，音调不得进入 LLM 请求体。

## Goals / Non-Goals

**Goals:**
- 构建固定上游版本的离线 Kanjium 音调 SQLite 索引 `pitch-accent.db`，与 `jmdict.db` 保持一致的准备、发布强校验、按需只读访问与第三方许可合规机制。
- 建立“先确定有效读音，再按确定性分层解析整词音调”的离线管线：**人工校正表 → Kanjium SQLite `(表记, 有效读音)` 精确匹配 → 首批 5 类高确定性助数词规则 → 既有 UniDic 源词元 / 单 token 单词参考降级**。
- 在词块分组阶段补齐半角/全角阿拉伯数字归一化查词与 5 类白名单助数词（`週間`、`時間`、`年生`、`ヶ月/か月/ヵ月/カ月/箇月`、`曜日`）的可靠合并及读音推导。
- 引入整词音调数据模型 `SpanPitch`（保留有序多候选与来源），打通覆盖层整 span 跨行 H/L 上色（多候选取第一候选）与弹窗全候选展示，同时守住单 token 单词参考不染整组覆盖层的边界。
- 保持核心解析接口与整词渲染管线解耦，使后续 Phase 2 的 UniDic `AConType`/`AModType` 活用结合引擎可直接作为一个解析层接入。

**Non-Goals:**
- 本次变更不实现 UniDic `AConType`/`AModType` 动词/形容词活用及一般复合词结合引擎（留待 Phase 2）。
- 不引入跨词块句法变调（如 `尾高 + の` 平板化、`〜の上/下/人`、副词化变调）。
- 不引入 Gist 末尾的统计启发式猜测（外来语拍数规律、二字汉语趋势、人名默认倒数第三拍），避免向用户展示低置信度猜测。
- 不修改 LLM prompt、schema 或 provider 请求体，不增加任何云端 token 消耗。

## Decisions

### 1. 离线 SQLite 索引结构与构建流程（`pitch-accent.db`）
- **决策**：新增 `tools/PitchAccentIndexBuilder` 与 `scripts/Prepare-PitchAccent.ps1`，将固定提交版本的 Kanjium `accents.txt` 转换为只读 SQLite 文件 `src/KotobaSenpai.App/Assets/Dictionary/pitch-accent.db`，发布时复制到程序根目录 `pitch-accent.db`（并在 `KotobaSenpai.App.csproj` 与 `scripts/Build-Installer.ps1` 强制校验存在性）。
- **表结构**：
  ```sql
  CREATE TABLE meta (
      key   TEXT PRIMARY KEY,
      value TEXT NOT NULL
  );
  CREATE TABLE entries (
      surface   TEXT NOT NULL,
      reading   TEXT NOT NULL,
      positions TEXT NOT NULL, -- 有序去重整数列表，如 "3" 或 "0,3"
      PRIMARY KEY (surface, reading)
  ) WITHOUT ROWID;
  ```
- **解析细节**：
  - Kanjium `accents.txt` 行格式为 `surface<TAB>reading<TAB>accents`。当 `reading` 列为空（纯假名词条）时，以 `Kana.ToHiragana(surface)` 作为 `reading`。
  - `reading` 统一转为平假名并去除非假名标注符号；`accents` 按 `,` 拆分，剥离可选词性前缀（如 `(副)`）后提取非负整数并按拍数边界（`0 <= pos <= moraCount`）过滤，保持原顺序去重。
  - 若同一 `(surface, reading)` 在源文件出现多行，按出现顺序合并去重 `positions`。
- **运行时读取**：在 `KotobaSenpai.Platform.Windows/Dictionary/PitchAccentSqliteRepository.cs` 实现 `IPitchAccentDictionary`，沿用 `JmdictSqliteRepository` 的 `Pooling=False` 按需开闭连接、批量分块查询（一次识别仅一次批量查询）及缺失时记一次日志并返回空结果的优雅降级策略。
- **许可合规**：在 `THIRD-PARTY-NOTICES` 及 `Assets/Dictionary/` 随包附上 Kanjium 上游仓库地址、固定 commit/版本及 CC BY-SA 4.0 许可与署名文本（与 `JMdict-NOTICE.txt` / `JMdict-LICENSE.txt` 一致）。
- **备选方案与取舍**：曾考虑直接打包 `accents.txt` 启动时全量加载进内存字典；但 SQLite 与现有 `jmdict.db` 完全一致、常驻内存开销近零、查询按需按批完成且无需每次启动解析 12 万行文本，综合体验更优。

### 2. 分层离线音调解析管线与优先级
- **决策**：在 `KotobaSenpai.Core` 新增纯逻辑服务 `SpanPitchResolver`（实现 `ISpanPitchResolver`），对任意 `GroupedWord` 在确定有效读音后按以下顺序解析：
  1. **L1 项目人工校正表（`ManualPitchOverrides`）**：静态只读表，键为 `(normalizedSurface, hiraganaReading)`，仅收录经权威来源（NHK / 词典）核对的已知误标或歧义修正；普通已准确收录词（如 `一週間`）不写入此表，直接交由 L2 命中。
  2. **L2 Kanjium SQLite 精确匹配（`IPitchAccentDictionary`）**：使用 `(Surface, hiraganaReading)` 严格精确查询；若表记含半角/全角阿拉伯数字，按确定顺序尝试原表记与汉数字规范化表记（如 `1週間` → `一週間`），但**读音始终必须与有效读音精确匹配**，绝不退化为“仅按表记取首条记录”（防止 `頭(あたま)` 与 `頭(こうべ)` 串调）。同时只查出现形 `Surface`，绝不用 `LookupKey` 基本形去套活用形（防止把 `食べる[2]` 错误套用到 `食べて`）。
  3. **L3 白名单助数词规则（`WhitelistedCounterPitchRules`）**：对命中 5 类白名单助数词且读音合法对齐的词块，按 `前部拍数 + 1` 计算整词下降核位置。
  4. **L4 降级路径**：
     - 若词块未发生读音覆盖（`!HasOverriddenReading`）：继续保留 `SourceTokens` 各自的 UniDic `PitchAccents`；
     - 若词块发生读音覆盖（`HasOverriddenReading`）且 L1–L3 均未命中整词音调：回退至既有 `CorrectedWordPitch` 单 token 单词回查（如 `頭を垂れる` 中的 `頭（こうべ）`），仅作为弹窗的单词参考（`WordPitch`），覆盖层不着色。
- **Phase 2 扩展位**：L2 与 L3 之间（或作为 L3 的兄弟策略）可自然插入 `IMorphologicalPitchResolver`（基于 UniDic `AConType`/`AModType`），产出同样的 `SpanPitch` 记录，无需修改渲染与弹窗层。

### 3. 数字规范化与 5 类高频助数词白名单
- **决策**：
  - **数字规范化用于查词与查音调，不改写原始 `Surface`**：将 `0–9` / `０–９` 的简单数词片段规范化为汉数字（`1–10` 及常见两位数如 `11–99`）生成附加查词候选，使 `1週間` 在 `TokenBoundarySpanResolver` 中可命中 JMdict 的 `一週間` 并获得唯一有效读音 `いっしゅうかん`，同时保留界面显示的原 OCR 文本 `1週間`。
  - **首批 5 类白名单助数词（均属“助数词第一拍后下降”，即 `prefixMoraCount + 1`）**：
    | 助数词表记 | 助数词读音 | 合法前部 | 典型音变读音处理 |
    |---|---|---|---|
    | `週間` | `しゅうかん` | 数词（1–99、汉/半角/全角、`何`） | `一→いっ`、`八→はっ/はち`、`十→じゅっ/じっ` |
    | `時間` | `じかん` | 数词（1–99、`何`） | `四→よ`、`九→く`、`七→しち/なな` |
    | `年生` | `ねんせい` | 数词（1–6 及常见年级数、`何`） | `四→よ` |
    | `ヶ月` / `か月` / `ヵ月` / `カ月` / `箇月` | `かげつ` | 数词（1–99、`何`） | `一→いっ`、`六→ろっ`、`八→はっ`、`十→じゅっ/じっ` |
    | `曜日` | `ようび` | 星期前缀（`月/火/水/木/金/土/日/何`） | `月→げつ`、`火→か`、`水→すい`、`木→もく`、`金→きん`、`土→ど`、`日→にち`、`何→なん` |
  - **两条规则生效方式**：
    1. **词块与读音缺失时（如 `三週間`、`3ヶ月`）**：若 JMdict 未命中整词，但连续源 token 严格匹配 `[合法前部] + [白名单助数词]`，则 `TokenBoundarySpanResolver` 将其合并为单个 `LookupSpan` 并推导标准有效读音（如 `さんしゅうかん`、`さんかげつ`）。仅对明确支持的前部数值推导读音，超出可确定范围（如小数、超大数或歧义读法）不强行合成。
    2. **音调核计算时**：只要词块表记匹配白名单助数词且有效读音以对应助数词平假名结尾、前部读音非空（无论有效读音来自 JMdict、助数词推导还是 LLM 纠偏，如 `二週間 / にしゅうかん`），均直接以 `SplitMorae(prefixReading).Count + 1` 计算核位置并校验 `position <= totalMoraCount`。

### 4. 数据模型与证据分级展示（覆盖层 & 弹窗）
- **核心模型**：
  ```csharp
  public enum SpanPitchSource
  {
      ManualOverride,
      Dictionary,
      CounterRule,
  }

  public sealed record SpanPitch(
      string Reading,
      IReadOnlyList<int> Positions,
      SpanPitchSource Source);
  ```
  - `GroupedWord` 新增 `SpanPitch? SpanPitch`；`WordMeaningView` 同步携带 `SpanPitch? SpanPitch`，经 `WordOverlaySession` 传入 `OverlayAnnotationLayer` 与 `PhrasePopupViewModel`。
  - `WordOverlayApplicationService` 在两个时机批量解析 `SpanPitch`：
    1. `PreparePublication`：对整帧所有 `GroupedWord` 一次性批量查询 `IPitchAccentDictionary` 并解析 `SpanPitch`（未命中时若发生读音覆盖再回退 `CorrectedWordPitch`）；
    2. `ApplyReadingOverrides`：对 LLM 改变有效读音的词块重新执行整词音调解析（如 `額: がく → ひたい` 立即命中 Kanjium `[0]`）。
- **覆盖层渲染策略（`OverlayAnnotationLayer`）**：
  - 当 `word.SpanPitch is { Positions.Count: > 0 } spanPitch` 时：
    - 与现有 UniDic 多候选策略一致，取 `spanPitch.Positions[0]` 构建整个词块的 `PitchAccentPattern`。
    - 通过 `FuriganaSettings.GetFuriganaSegments(word.Surface, word.Reading)` 切分汉字段与假名段，并统一调用 `FuriganaLayout.GetSpanParts` 计算每行几何框，为汉字段绘制红/蓝 H/L 振假名，为假名段按拍绘制红/蓝圆点，平板型（`0` 且拍数 > 1）在末拍绘制平板尾标。
    - 跨行词块（如跨越两行 OCR 文本的复合词）共享同一个 `PitchAccentPattern` 与拍偏移，降核位置准确落在实际对应拍上。
  - 当 `word.SpanPitch` 为 `null` 且 `HasOverriddenReading` 为 `true`（含仅有单 token `WordPitch` 参考的 `頭を垂れる`）时，保持单色振假名不画圆点。
  - 更新 `OverlayAnnotationLayer.Matches(...)` 纳入 `SpanPitch` 比较，确保 LLM 读音纠偏后覆盖层仅局部刷新受影响的词块。
- **弹窗展示策略（`PhrasePopupViewModel`）**：
  - 当 `word.SpanPitch is { Positions.Count: > 0 }` 时：
    - 右上角圆圈数字使用第一候选 `Positions[0]`；
    - 音调卡片展示所有合法候选的高低音序列（如单候选 `LH↓H`，多候选 `LHH / LHH↓`），避免隐藏其他常见标准读法。
  - 当无 `SpanPitch` 且有单 token `WordPitch` 参考时，沿用既有“UniDic 单词音调 `Surface（Reading） [pos] ...`”格式且不生成词头圆圈数字。
  - 两者皆无且无源 token 有效音调时显示“音调未知”。

## Risks / Trade-offs

- **[Risk] Kanjium `accents.txt` 中部分词条的读音列为空或带有词性/发声注释符号，直接解析可能丢失或错位** → **Mitigation**：在 `PitchAccentIndexBuilder` 中显式处理空读音列（回退表记平假名）、剥离括号词性标签与非假名符号，按 `PitchAccent.SplitMorae` 校验 `0 <= pos <= moraCount`，并产出统计摘要与单元测试覆盖异常行格式。
- **[Risk] 阿拉伯数字在部分场景（如日期、版本号、型号 `3D`、`2026`）不应被盲目转为汉数字或强行合并** → **Mitigation**：数字规范化仅作用于紧邻白名单助数词或命中 JMdict 词条的纯数词片段，且限制在明确可安全推导读音的范围内（如 1–99 整数），超出范围直接跳过，不改写原始 `Surface`。
- **[Risk] Kanjium 词典中的普通单词可能与 UniDic `aType` 存在细微差异（尤其单 token 词）** → **Mitigation**：Kanjium 来自广受日语学习社区检验的标准声调汇总（与 DokiDokiDict 同源），在 `(Surface, Reading)` 精确匹配的前提下准确度通常优于 UniDic 短单位单值；对确有争议的个案可通过 L1 `ManualPitchOverrides` 精准兜底，并在测试中建立回归基线。
- **[Risk] 打包新增 `pitch-accent.db` 会增加安装包体积并影响现有发布脚本与 CI** → **Mitigation**：采用 `WITHOUT ROWID` 紧凑单表索引（预期约 3–6 MB），沿用 `Prepare-Jmdict.ps1` 与 `Build-Installer.ps1` 的模式（支持 `-PitchAccentDatabase` 参数复现与离线构建），并在开发环境下缺失时安全降级、不阻塞无索引的单元测试。
- **[Risk] 整词 span 跨行上色时，若表记含汉字与假名混排且跨行切分，拍偏移若按行重算会导致高低音错位** → **Mitigation**：在 `OverlayAnnotationLayer` 中对整个 span 维护单调递增的全局 `moraOffset`，每个段经 `FuriganaLayout.GetSpanParts` 拆分出的每行 `part` 以 `moraOffset + part.MoraStart` 索引同一份 `PitchAccentPattern`。

## Migration Plan

1. 开发环境新增 `tools/PitchAccentIndexBuilder` 与 `scripts/Prepare-PitchAccent.ps1`，固定 Kanjium 上游 commit 与 SHA-256 并生成本地 `Assets/Dictionary/pitch-accent.db`。
2. 补齐 `THIRD-PARTY-NOTICES`、`Assets/Dictionary/Kanjium-NOTICE.txt`、`Kanjium-LICENSE.txt` 及 `KotobaSenpai.App.csproj` / `Build-Installer.ps1` 的发布强校验。
3. 本次为纯只读离线资产与本地展示升级，不涉及用户 `settings.json` 破坏性迁移；若需回滚，仅需回退代码并移除 `pitch-accent.db`，既有 UniDic 管线可独立运行。
