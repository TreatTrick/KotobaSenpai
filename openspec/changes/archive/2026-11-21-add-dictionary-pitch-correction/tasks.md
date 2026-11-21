## 1. 离线 Kanjium 音调 SQLite 索引工具与打包合规

- [x] 1.1 新增 `tools/PitchAccentIndexBuilder` 控制台工具，解析固定上游版本的 Kanjium `accents.txt`（处理空读音列回退平假名、剥离词性前缀/非假名符号、按拍数校验并按序去重 `positions`），生成 `WITHOUT ROWID` 复合主键 `(surface, reading)` 的只读 SQLite 文件 `pitch-accent.db` 及 `meta` 元数据表。
- [x] 1.2 新增 `scripts/Prepare-PitchAccent.ps1`（支持 `-DatabasePath` 与 `-SourcePath` 参数，对齐 `Prepare-Jmdict.ps1` 的原子写入模式），产出 `src/KotobaSenpai.App/Assets/Dictionary/pitch-accent.db`。
- [x] 1.3 新增 `src/KotobaSenpai.App/Assets/Dictionary/Kanjium-NOTICE.txt`、`Kanjium-LICENSE.txt`（CC BY-SA 4.0）并更新根目录 `THIRD-PARTY-NOTICES` 登记上游来源、固定版本及许可声明。
- [x] 1.4 更新 `src/KotobaSenpai.App/KotobaSenpai.App.csproj` 与 `scripts/Build-Installer.ps1`，在构建/发布阶段输出程序根目录 `pitch-accent.db` 及许可文件，并在 `PrepareForPublish` 与安装包构建脚本中强制校验 `pitch-accent.db` 存在性。

## 2. Core 音调模型、数字规范化与白名单助数词规则

- [x] 2.1 在 `KotobaSenpai.Core` 新增 `SpanPitchSource`、`SpanPitch`、`IPitchAccentDictionary` 与 `ISpanPitchResolver` 接口及模型，确保全部新增代码与注释遵循英文规范。
- [x] 2.2 在 `KotobaSenpai.Core` 实现数词规范化与首批 5 类白名单助数词辅助逻辑（`週間`、`時間`、`年生`、`ヶ月/か月/ヵ月/カ月/箇月`、`曜日`）：支持半角/全角阿拉伯数字 1–99 规范化为汉数字查词键、标准音变读音推导（含促音化与 `よ/く` 特殊读音）及 `prefixMoraCount + 1` 核位置计算。
- [x] 2.3 在 `KotobaSenpai.Core` 实现 `ManualPitchOverrides` 静态精确覆盖表与 `SpanPitchResolver` 分层解析管线（`人工校正表 → IPitchAccentDictionary 精确匹配（含数字规范化） → 白名单助数词规则`），严格以 `(Surface, EffectiveReading)` 匹配，杜绝用 `LookupKey` 基本形误套活用形。
- [x] 2.4 扩展 `TokenBoundarySpanResolver`：对紧邻汉字词元的 1–99 阿拉伯数字生成汉数字归一化查词候选（保留原 OCR `Surface`），并在 JMdict 缺整词条目时可靠合并白名单助数词序列并推导有效读音（如 `1週間`、`三週間`、`3ヶ月`）。

## 3. Platform SQLite 仓储与识别会话管线集成

- [x] 3.1 在 `KotobaSenpai.Platform.Windows/Dictionary` 实现 `PitchAccentSqliteRepository`（`IPitchAccentDictionary`），采用 `Pooling=False` 按需连接、分块批量精确查询 `(surface, reading)`，并在索引缺失或查询失败时记一次警告日志优雅返回空结果。
- [x] 3.2 扩展 `GroupedWord`、`WordMeaningView`、`WordOverlaySession` 传播 `SpanPitch`，并在 `WordOverlayApplicationService` 的 `PreparePublication`（单次批量解析）和 `ApplyReadingOverrides`（LLM 纠偏后重解析）中调用 `WordPitchResolver`，由其统一协调 `ISpanPitchResolver` 和 UniDic 回退；确保仅在整词未命中且发生读音覆盖时才回退至单 token `CorrectedWordPitch` 单词参考，且所有音调字段不进入 LLM 请求体。
- [x] 3.3 在 `DesktopComposition` 中注册 `PitchAccentSqliteRepository`（读取 `AppContext.BaseDirectory/pitch-accent.db`）与 `WordGrouping` 的音调解析服务，并将 `WordPitchResolver` 注入 `WordOverlayApplicationService`。

## 4. 覆盖层整词跨行着色与弹窗全候选展示

- [x] 4.1 扩展 `OverlayAnnotationLayer`：当 `word.SpanPitch` 存在时取第一候选构建整词 `PitchAccentPattern`，结合 `FuriganaSettings.GetFuriganaSegments` 与 `FuriganaLayout.GetSpanParts` 实现整 span 跨行统一 H/L 振假名着色、假名圆点与平板尾标，并更新 `Matches(...)` 纳入 `SpanPitch` 比较以支持局部刷新。
- [x] 4.2 验证仅有单 token `WordPitch` 参考而无 `SpanPitch` 的覆盖读音词块（如 `頭を垂れる`）在覆盖层继续保持单色振假名且不绘制过期圆点。
- [x] 4.3 更新 `PhrasePopupViewModel`：存在 `SpanPitch` 时词头右上角圆圈数字使用第一候选，音调卡片按顺序展示全部候选高低音序列（以 ` / ` 分隔）；无 `SpanPitch` 且存在 `WordPitch` 时保持既有单 token 单词参考文案且不生成整词圆圈数字。

## 5. 测试与端到端基线验证

- [x] 5.1 编写 `PitchAccentIndexBuilder` 与 `PitchAccentSqliteRepository` 单元测试（覆盖常规行、空读音纯假名行、词性前缀行、同形异读隔离、批量查询与缺失文件优雅降级）。
- [x] 5.2 编写 `TokenBoundarySpanResolver` 与 `SpanPitchResolver` 单元测试（覆盖人工校正优先、`一週間`/`1週間` 词典命中、`二週間`/`三週間`/`四時間`/`三年生`/`三ヶ月`/`火曜日` 白名单助数词、`額(ひたい)` LLM 纠偏重解析、`食べて` 不误套 `食べる`、非白名单 `三冊` 返回未知）。
- [x] 5.3 编写 `OverlayAnnotationLayer` 与 `PhrasePopupViewModel` 测试（覆盖整词跨行上色、整词多候选首候选上色+弹窗全候选、`頭を垂れる` 仅弹窗单词参考不染整组覆盖层、LLM 纠偏后局部刷新）。
- [x] 5.4 运行完整 `dotnet build`、`dotnet test` 与本地 `Prepare-PitchAccent.ps1` 验证，确认全部测试通过且无警告错误。

Verification: the solution build completed with zero warnings and errors. All 1,348 tests passed across two invocations. The existing `Open_popup_follows_application_theme_changes_without_replacing_content` test reproduced Win32 error 1400 in a full run on both `HEAD` and the change, and passed when run alone.

```powershell
dotnet test KotobaSenpai.slnx --no-build --nologo -m:1 --filter 'FullyQualifiedName!~Open_popup_follows_application_theme_changes_without_replacing_content'
dotnet test tests/KotobaSenpai.App.Tests --no-build --nologo --filter 'FullyQualifiedName~PhrasePopupThemeTests.Open_popup_follows_application_theme_changes_without_replacing_content'
```
