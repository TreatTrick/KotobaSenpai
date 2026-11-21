## Why

KotobaSenpai 目前仅依赖 UniDic 单语素的孤立 `aType` 显示音调：一旦复合词被 UniDic 切碎（如 `一週間` 被拆为 `一 + 週間`）、半角数字与词典表记不一致（如 `1週間`）、或有效读音被 JMdict / LLM 纠偏覆盖（如 `額: がく → ひたい`），覆盖层就会退化为单色振假名或显示割裂的原语素音调；同时，词典未收录的数词＋助数词组合（如 `二週間`、`三週間`）也完全缺失整词音调。

引入一套离线优先的整词音调校正体系（人工校正表 → Kanjium SQLite 精确匹配 → 已核对的高频助数词规则），并打通整词跨行覆盖层着色与弹窗全候选展示，可以在零 LLM Token 成本下显著提升视觉小说阅读场景的音调覆盖率与准确度，同时为后续 Phase 2 的 UniDic `AConType`/`AModType` 活用结合引擎奠定统一的整词音调模型与渲染管线。

## What Changes

- **离线 Kanjium 音调词典 SQLite 资产与构建发布链**：新增构建期工具与准备脚本，将固定上游版本的 Kanjium `accents.txt` 预处理为只读 SQLite 索引 `pitch-accent.db`，随应用输出至程序根目录并在发布/安装包构建阶段强校验存在性；同步在 `THIRD-PARTY-NOTICES` 及发布文件补充 CC BY-SA 4.0 许可与署名。
- **分层离线整词音调解析管线**：在确定词块有效读音（含 JMdict 回流、助数词推导、LLM 语境纠偏）之后，按 **`项目人工校正表` → `Kanjium SQLite (表记, 有效读音) 精确匹配` → `已核对的白名单助数词规则` → `现有 UniDic 源词元 / 单词元回查降级`** 的优先级解析音调，严格要求表记与有效读音同时精确匹配，杜绝仅按表记误取异读音调或拿原形直接套用活用形。
- **数字规范化与首批高频助数词白名单**：在词块解析中支持半角/全角阿拉伯数字归一化查词（如 `1週間`、`１週間` 复用 `一週間`），并为经 NHK / 权威教材核对的 5 类高频助数词（`週間`、`時間`、`年生`、`ヶ月/か月/ヵ月/カ月/箇月`、`曜日`）提供稳定的词块合并、有效读音推导与降核位置计算（`前部拍数 + 1`）。
- **整词音调数据模型与跨行覆盖层着色**：在 `GroupedWord` 与 `WordMeaningView` 中引入整词级音调结果（保留多候选与来源元数据），并扩展 `OverlayAnnotationLayer` 复用 `FuriganaLayout.GetSpanParts` 实现整词跨行统一 H/L 着色、假名圆点与平板尾标；多候选时与现有 UniDic 策略保持一致——覆盖层取第一候选上色，弹窗列出全部候选。仅有单 token 单词参考证据（如 `頭を垂れる` 仅匹配 `頭（こうべ）`）时仍保持覆盖层单色、弹窗标明单词参考。
- **范围边界（Phase 1）**：本次变更不实现 UniDic `AConType`/`AModType` 活用结合引擎，不引入启发式统计猜测（如外来语/人名拍数趋势），也不修改 LLM 请求体结构；但在核心抽象与整词渲染管道上为 Phase 2 预留干净的解析器扩展点。

## Capabilities

### New Capabilities
- `pitch-accent-dictionary`: 定义离线 Kanjium 音调词典 SQLite 索引（`pitch-accent.db`）的构建、捆绑发布强校验、按 `(表记, 有效读音)` 精确查询及缺失优雅降级契约，以及项目人工校正表与白名单助数词规则解析契约。

### Modified Capabilities
- `word-grouping`: 扩展词块合并与有效读音推导，支持半角/全角数字归一化匹配词典及白名单助数词（`週間`、`時間`、`年生`、`ヶ月` 变体、`曜日`）在 JMdict 缺整词条目时的可靠合并与读音生成。
- `pitch-accent-display`: 将音调解析升级为整词优先的分层离线管线，支持在读音经 JMdict / 助数词规则 / LLM 覆盖后重新解析整词音调，保留多候选与证据范围边界，同时延续“不伪造未知音调、不进入 LLM 请求体”的红线。
- `furigana-overlay`: 支持对命中整词音调（含覆盖读音后重新命中的整词音调）的词块按整 span 跨行统一绘制 H/L 振假名、假名圆点与平板尾标（多候选取第一候选）；无整词音调或仅有单 token 单词参考时维持单色振假名。
- `phrase-popup-presentation`: 在单词详情与短语成员词卡片中展示整词音调的主型别圆圈数字及全部候选高低音序列；当仅有单 token 单词参考时继续明确标注单词来源而不伪造整组圆圈数字。

## Impact

- **Core (`KotobaSenpai.Core`)**：新增整词音调解析接口、结果模型（含多候选与证据级别）、人工校正表与助数词规则实现；扩展 `TokenBoundarySpanResolver`、`LookupSpan`、`GroupedWord`、`WordMeaningView`、`WordOverlaySession` 与 `WordOverlayApplicationService`。
- **Platform (`KotobaSenpai.Platform.Windows`)**：新增 `PitchAccentSqliteRepository`（按需只读访问 `pitch-accent.db`，风格与 `JmdictSqliteRepository` 一致）。
- **Presentation (`KotobaSenpai.Presentation.Wpf`)**：扩展 `OverlayAnnotationLayer` 支持整词 span 级跨行音调着色与圆点绘制；更新 `PhrasePopupViewModel` 展示整词多候选及来源区分。
- **App / Tooling / Packaging**：新增 `tools/PitchAccentIndexBuilder`、`scripts/Prepare-PitchAccent.ps1`，更新 `KotobaSenpai.App.csproj`、`DesktopComposition.cs`、`scripts/Build-Installer.ps1`、`THIRD-PARTY-NOTICES` 及本地化资源字符串（如有新增来源文案）。
- **Tests (`tests/`)**：新增词典查询、人工校正优先级、数字规范化、5 类助数词读音与核位置、整词跨行覆盖层渲染及弹窗全候选展示的单元与集成测试（覆盖 `一週間` / `1週間` / `二週間` / `三週間` / `一時間` / `額(ひたい)` / `頭を垂れる` 等基线用例）。
