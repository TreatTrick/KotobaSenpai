## 1. JMdict 打包与发布构建校验（P0）

- [x] 1.1 新增 `scripts/Prepare-Jmdict.ps1`（支持 `-DatabasePath` 复用本地 `jmdict.db`，缺省时通过 `tools/JmdictIndexBuilder` 构建），输出至 `src/KotobaSenpai.App/Assets/Dictionary/jmdict.db`，并在 `.gitignore` 中忽略该大体积二进制资产。
- [x] 1.2 在 `src/KotobaSenpai.App/KotobaSenpai.App.csproj` 中添加条件 `<Content>` 将 `Assets\Dictionary\jmdict.db` 复制到输出和发布根目录 `jmdict.db`，并新增 `BeforeTargets="PrepareForPublish"` 的 `RequireBundledJmdict` 校验目标。
- [x] 1.3 更新 `scripts/Build-Installer.ps1`，增加可选 `-JmdictDatabase` 参数并调用 `Prepare-Jmdict.ps1`，在 `dotnet publish` 后强制校验 `$publishDirectory\jmdict.db` 存在再编译 Inno Setup 安装包。
- [x] 1.4 确认 `JmdictSqliteRepository` 在非发布环境缺少 `jmdict.db` 时仍安全降级为空结果且记录清晰诊断日志。

## 2. 多词 JMdict 活用匹配与唯一读音回流（P0 + P1）

- [x] 2.1 在 `KotobaSenpai.Core.Dictionary` 中为 `LookupSpan` 增加读音覆盖标记（如 `HasOverriddenReading`）及可选覆盖读音构造支持：读音被覆盖时使用有效读音并清空 `PitchAccents` 抑制过期 UniDic 音调，未覆盖时完全保持既有行为。
- [x] 2.2 扩展 `TokenBoundarySpanResolver`：对含至少一个汉字的多 token 候选，当末尾 token 为可活用动词/形容词且 `Surface != OrthBase/Lemma` 时生成辞书形候选批量查词，命中后通过 `FindInflectionEnd` 吸收后续助动词及接续助词 `て`/`で`（如 `頭を垂れた`、`踵を返して`），且纯假名序列不触发末尾辞书形归一化。
- [x] 2.3 在 `TokenBoundarySpanResolver` 中实现保守的 JMdict 唯一读音回流逻辑：仅对含汉字的多 token 命中且词典读音去重后唯一时推导有效读音（活用词块校验词典读音以末尾词元 `BaseReading` 结尾后再替换出现形并拼接尾假名，无法安全对齐时回退 UniDic 原读音）。
- [x] 2.4 补充 `TokenBoundarySpanResolver` 与 `LookupSpan` 单元测试：覆盖 `頭を垂れる`、`頭を垂れた`、`踵を返す`、`踵を返して`、`一週間`、`目の当たり`、多读音歧义词 `額`、单 token 动词活用（不被覆盖）、纯假名不误合以及不规则活用安全回退。

## 3. 整 Span 振假名渲染与过期音调抑制（P0 + P2 基础）

- [x] 3.1 在 `KotobaSenpai.Core.WordGrouping` 中为 `GroupedWord` 传递并暴露 `HasOverriddenReading`、句段内词块顺序索引 `SegmentSpanIndex`，并提供不可变更新方法 `WithOverriddenReading(string reading)`（覆盖读音同时清空展示音调，且严格保持第 5 层不依赖 `PhraseAnalysis`）。
- [x] 3.2 更新 `WordGroupingService` 与 `SentenceTokenBuilder`，确保同一句段内 `GroupedWord.SegmentSpanIndex` 与 `LocalSpanSummary.SpanIndex` 严格一一对应，且 `LocalSpanSummary` 携带经过 JMdict 回流后的本地有效读音。
- [x] 3.3 扩展 `FuriganaLayout`：新增基于展平 `SourceTokenLines` 的整 span 几何映射方法，按源 token 精确字符区间将整 span 的 `FuriganaSegment` 映射到单行或跨行屏幕矩形。
- [x] 3.4 更新 `OverlayAnnotationLayer`：当 `word.HasOverriddenReading == true` 时走整 span 单色振假名绘制分支（不绘制过期音调圆点与平板尾标）；更新 `Matches(...)` 纳入 `HasOverriddenReading` 比较，确保云端刷新仅重绘读音或下划线发生变化的词。
- [x] 3.5 补充 `FuriganaLayout`、`FuriganaSettings` 与 `OverlayAnnotationLayer` 单元测试：验证 `頭を垂れる`、`頭を垂れた`、`踵を返して`、`一週間`、跨行覆盖读音词块以及局部重绘复用行为。

## 4. LLM 文本 JSON 容错提取与回退端点缓存（P0）

- [x] 4.1 在 `KotobaSenpai.Platform.Windows.Llm` 中新增 `LlmJsonExtractor`：优先标准严格 `JsonDocument.Parse`；失败时依次支持剥离单一 Markdown 代码围栏，以及基于 `Utf8JsonReader` 定位第一个完整平衡根 JSON 对象并校验剩余尾部仅含空白与多余 `}`（返回容错说明供 Warning 日志记录）；对截断、语法错误或夹带额外文本/多对象的响应仍严格抛错。
- [x] 4.2 将 `OpenAiResponsesProtocol.ExtractContentRoot` 与 `OpenAiChatCompletionsProtocol.ExtractContentRoot` 接入 `LlmJsonExtractor`，并在 `LlmPhraseAnalyzer` 中记录容错恢复 Warning 日志。
- [x] 4.3 在 `LlmPhraseAnalyzer` 中新增按 `(ProtocolId, ConfiguredEndpoint)` 键控的线程安全内存回退端点缓存：当 `singleEndpoint == false` 时将已验证成功的候选 URI 提前到首位；命中地址若后续返回 HTTP 404/405 则立即驱逐并顺次尝试其余候选；`singleEndpoint == true` 保持不读不写缓存。
- [x] 4.4 补充 `LlmJsonExtractor`、协议解析与 `LlmPhraseAnalyzer` 单元测试：覆盖尾随多余 `}`（含 `目の当たり` 真实故障样本）、Markdown 围栏、截断/双对象拒绝、首次 404 回退后二次请求免 404、缓存地址失效驱逐、配置变更隔离及 `singleEndpoint` 隔离。

## 5. LLM 按需语境读音纠偏全链路（P2）

- [x] 5.1 更新 `PhraseGroupSchema`、`OpenAiChatCompletionsProtocol` 示例和 `PhrasePromptBuilder`：在 `words[]` 中增加字符串字段 `reading`（要求本地读音正确时返回 `""`，仅在语境读音不同时返回覆盖整词的纯平假名），三协议共享一致语义；更新 `PhraseResponseParser.ParseWords` 以兼容缺省 `reading` 字段的旧/宽松响应。
- [x] 5.2 更新 `ParsedWordMeaning`、`WordMeaningView` 与 `WordMeaningValidator`：
  - 将 `localSpans` 按 `Surface` 建序队列以支持同句重复表记按出现次序绑定（超出本地出现次数的多余重复项仍丢弃）；
  - 独立校验 `reading`（转平假名、长度 1–64、全合法假名、忽略纯假名词及与本地读音相同的值），非法 `reading` 仅丢弃读音覆盖并记录警告，不连坐合法 `pos`/`meaning`/`grammar`；
  - 有效读音覆盖时更新 `WordMeaningView.Reading`、置 `HasOverriddenReading = true` 并清空 `PitchAccents`。
- [x] 5.3 更新 `PhraseRunAssembler`（透传 `SegmentId` / `SpanIndex` 且兼容增量缓存重绑定）、`WordOverlaySession`（优先按 `(SegmentId, SegmentSpanIndex)` 查找词义并保留 `(Surface, Reading)` 兜底）以及 `WordOverlayApplicationService`（在 `ShowPreview` 缓存复用和 `AnalyzeAndPublishAsync` 发布前将已校验 LLM 读音覆盖应用到 `GroupedWord`）。
- [x] 5.4 检查并更新 `PhrasePopupViewModel`、`OverlayHoverController` 与 `FileDiagnosticReporter`：确保悬停单字弹窗和短语组内词表均展示最终有效读音及对应罗马音、读音被覆盖时显示未知音调，且诊断文件清晰输出最终有效读音与覆盖状态。
- [x] 5.5 补充 Core、Platform.Windows 与 Presentation.Wpf 端到端测试：覆盖 `額に汗をかく`（`ひたい`）与 `支払う額`（`がく`）、同句双 `額` 分别纠偏、非法 `reading` 降级保词义、纯假名词忽略纠偏、缓存跨轮保留纠偏读音、以及旧响应缺失 `reading` 向后兼容。

## 6. 全量回归与验收验证

- [ ] 6.1 运行 `dotnet test` 验证全部单元测试与 `KotobaSenpai.Architecture.Tests` 架构分层测试零警告零失败通过。
- [ ] 6.2 使用真实日语测试句（含 `額に汗をかく`、`支払う額`、`頭を垂れる`、`頭を垂れた`、`踵を返す`、`踵を返して`、`目の当たり`、`一週間`）开启 `DiagEnabled` 做端到端验证，确认 `diag/` 与覆盖层振假名、下划线、悬停弹窗与免重复 404 日志全部符合规格。
- [x] 6.3 运行安装包构建检查（确认发布目录与安装流程包含 `jmdict.db` 且缺失时校验失败）。

### 归档验收记录（2026-11-21）

- 6.1：全量复验 1,303 项通过，1 项桌面快捷键测试失败。当前桌面会话的 F1–F5 已被注册（Win32 1409），测试未能注册所需快捷键。70 项架构测试全部通过。
- 6.2：真实 OCR、LLM、覆盖层和悬停弹窗的人工端到端验收待执行。
- 6.3：Windows 安装包构建成功，构建脚本验证发布根目录存在 `jmdict.db`，Inno Setup 将发布目录文件打包到应用目录。隔离执行 `RequireBundledJmdict` 确认字典缺失时会报错并给出准备指引。
