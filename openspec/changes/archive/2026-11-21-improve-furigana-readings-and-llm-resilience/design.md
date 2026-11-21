## Context

KotobaSenpai 的振假名与词义展示采用两阶段管线：

1. **本地阶段（零网络延迟）**：`MeikiOcrWordRecognizer` 产出字符级 OCR 框 → `SentenceSegmenter` 切分句段 → `UniDicTokenizer`（LibNMeCab + UniDic 3.1.0 SUW 短单位词典）分词 → `TokenBoundarySpanResolver` 基于本地 `jmdict.db` 在完整 token 边界上做最长覆盖合并生成 `LookupSpan` → `WordGroupingService` 构造 `GroupedWord` → `OverlayAnnotationLayer` 立即渲染本地振假名与音调。
2. **云端阶段（并发按句段分析）**：`PhraseAnalysisOrchestrator` 将每个句段的原文、UniDic token 表与 `LocalSpans` 发给 LLM（OpenAI Responses / OpenAI Chat Completions / Anthropic Messages 三协议抽象），返回 `groups`、`words`、`translation`，并在单次覆盖层刷新中补齐下划线、短语组、释义与整句译文。

结合实测与 `diag`/`logs` 诊断，当前架构存在 4 个跨模块缺口：
- **打包缺口**：`DesktopComposition` 在运行目录查找 `jmdict.db`，但 `KotobaSenpai.App.csproj` 与 `scripts/Build-Installer.ps1` 未将其纳入发布包，导致已安装版本完全丧失多词词典合并能力。
- **本地读音与活用匹配缺口**：`TokenBoundarySpanResolver` 仅对多词字面拼接做精确匹配（无法命中 `頭を垂れた` / `踵を返して` 等活用形），且 `LookupSpan` 构造时丢弃了 `Entries` 中的 JMdict 词典读音，始终拼接 UniDic 原短单位读音；`OverlayAnnotationLayer` 也始终按 UniDic 原始 `SourceTokens` 逐 token 绘音。
- **语境纠音通道缺失**：UniDic 2-gram 无法跨助词感知语义（`額に汗をかく` 恒读 `がく`），而 `PhrasePromptBuilder` 与 `PhraseGroupSchema` 明确禁止 LLM 返回或修正读音；同时 `WordMeaningValidator` / `WordOverlaySession` 按单一 `Surface` 去重，无法区分同句中两次出现的同形异音词（如同一句里同时有 `額(ひたい)` 和 `額(がく)`）。
- **LLM 响应与端点韧性缺口**：`OpenAiResponsesProtocol` / `OpenAiChatCompletionsProtocol` 直接 `JsonDocument.Parse` 文本负载，遇 `deepseek-flash` 等模型偶发在合法 JSON 末尾多吐 `}` 时整段判失败（导致如 `目の当たり` 无下划线/无释义）；`LlmPhraseAnalyzer` 未复用已验证成功的回退端点 URI，每个句段每次都先向根路径发一次 404 请求。

架构硬约束：严格遵守 `core-slice-architecture` 的 0–7 层依赖顺序（尤其第 5 层的 `WordGrouping` 与 `PhraseAnalysis` 互不依赖；Core 保持零 WPF / HTTP / 文件系统依赖；`Platform.Windows` 不反向依赖 `App`）；所有新增 C# 代码、注释、日志、测试必须为英文（遵循 `AGENTS.md`）。

## Goals / Non-Goals

**Goals:**
- 安装包与发布产物必带 `jmdict.db`，并在构建阶段自动校验其存在性，确保本地多词合并在正式安装环境中始终生效。
- 在不破坏常规单 token 分词与动词活用读音的前提下，使多词含汉字的 JMdict 唯一命中（含原型与末尾动词/形容词活用形，如 `頭を垂れる` / `頭を垂れた`、`踵を返す` / `踵を返して`、`一週間`）自动回流词典读音，并在覆盖层按精确源 token 几何对齐显示振假名。
- 为全部三套 LLM 协议增加按需语境读音纠偏能力（`words[].reading`），在既有单次云端刷新中局部更新覆盖层振假名、悬停弹窗与短语组详情，并支持同句内同表记多处出现的独立绑定与缓存复用。
- 当词块读音被 JMdict 或 LLM 覆盖且偏离 UniDic 原读音时，统一抑制过期的 UniDic 音调数据，防止错误高低音配色/圆点误导学习者。
- 提升 LLM 文本 JSON 解析对尾随多余 `}` 及 Markdown 代码块包裹的容错率，并在内存按 `(protocolId, configuredEndpoint)` 复用已探测成功的回退分析端点以消除重复 404 往返。

**Non-Goals:**
- **不做本地数词 + 助数词硬编码促音便/连浊规则层**（明确排除：不编写针对 `一/六/八/十/百/千 + 助数词` 的规则表；`一週間` 等词典已有词条由 JMdict 读音回流解决，其余未收录数字量词组合由 LLM 语境读音纠偏自然覆盖）。
- **不更换或重训 OCR 模型**（不处理简体中文误识别问题，MeikiOCR 仍面向日语文本）。
- **不让 LLM 生成或推测音调核（Pitch Accent）**：LLM 仅负责在必要时纠正假名读音，不引入额外的 LLM 音调幻觉。
- **不引入新的网络轮次或独立纠音接口**：所有 LLM 读音纠偏复用于既有单次句段分析请求，不增加请求数或二次覆盖层刷新。

## Decisions

### Decision 1：JMdict 打包复用与 UniDic 对称的离线资产准备模式
- **方案**：新增 `scripts/Prepare-Jmdict.ps1`（支持 `-DatabasePath` 导入既有 `jmdict.db`，或缺失时调用 `tools/JmdictIndexBuilder` 构建），将成品落盘至 git 忽略的 `src/KotobaSenpai.App/Assets/Dictionary/jmdict.db`。在 `KotobaSenpai.App.csproj` 中增加条件 `<Content>` 将其复制到输出与发布根目录的 `jmdict.db`，并添加 `BeforeTargets="PrepareForPublish"` 的 `RequireBundledJmdict` 校验目标；`scripts/Build-Installer.ps1` 在发布后校验 `$publishDirectory\jmdict.db` 存在（Inno Setup 已全量打包 `PublishDir\*`）。
- **备选方案与取舍**：
  - *直接把 53 MB 的 `jmdict.db` 提交进 git*：会永久膨胀仓库体积，拒绝。
  - *首次运行时在线下载 `jmdict.db`*：与已确立的离线安装方向（见 UniDic 离线打包）背道而驰，拒绝。

### Decision 2：多词 JMdict 读音回流的保守触发条件与活用还原算法
- **方案**：在 `TokenBoundarySpanResolver` 与 `LookupSpan`（均位于第 4 层 `Dictionary`）中引入明确的读音来源标记（`HasOverriddenReading`）：
  1. **多词活用匹配**：遍历 `start..end`（`end > start`）构造多 token 候选时，除原有的完整字面拼接 `surface` 外，若末尾 token `segment[end]` 是可活用词（`IsInflectingBase`：`動詞` 或 `形容詞`）且 `OrthBase` / `Lemma` 与 `Surface` 不同、且前缀拼接含至少一个汉字，则额外构造辞书形候选 `prefixSurface + baseForm` 参与批量查询（仅限匹配含汉字词条，避免纯假名短串误合并，例如 `に + し` 误撞 `にし`）。命中后调用 `FindInflectionEnd(segment, end)` 向后吸收后续助动词及接续助词 `て/で`（如 `頭 + を + 垂れ + た`、`踵 + を + 返し + て`），形成覆盖完整出现形的 `LookupSpan`，`LookupKey` 记录命中的辞书形（`頭を垂れる` / `踵を返す`）。
  2. **读音回流条件（防误伤）**：仅在同时满足以下条件时用 JMdict 读音覆盖 UniDic 拼接读音：
     - `Tokens.Count > 1`（单 token 不走 JMdict 覆盖，避免辞书形覆盖动词活用读音或误选多音字）；
     - `Surface` 包含至少一个汉字（纯假名词无需覆盖）；
     - 匹配到的 JMdict 条目中，经平假名归一后所有非空 `Entry.Reading` 去重后**仅有唯一 1 个读音**（多义多读音不盲选，交给 LLM）；
     - 对于**直配多词**（无末尾活用变形）：词典读音 `dictReading` 直接作为候选读音；
     - 对于**活用多词**（末尾词元经 `OrthBase`/`Lemma` 命中并可选携带后续活用链 `tailTokens`）：检查 `dictReading` 是否以 `Kana.ToHiragana(baseToken.BaseReading)` 结尾。若是，则将末尾该段替换为 `Kana.ToHiragana(baseToken.Reading)` 并拼接所有 `tailTokens` 的发生形读音（如 `こうべをたれる` → 替换 `たれる` 为 `たれ` + `た` → `こうべをたれた`；`きびすをかえす` → 替换 `かえす` 为 `かえし` + `て` → `きびすをかえして`）。若不匹配（不规则变形），则安全回退到 UniDic 读音，交由二阶段 LLM 纠偏；
     - 最终候选读音（归一为平假名）与 UniDic 拼接读音（归一为平假名）不同，且通过假名合法性检查时，设置 `LookupSpan.Reading`（按项目内部习惯保留平假名/片假名一致规范，供下游 `Kana.ToHiragana` 消费）并标记 `HasOverriddenReading = true`，同时将 `LookupSpan.PitchAccents` 置空（避免 UniDic 旧音调错误叠加）。
- **备选方案与取舍**：
  - *单 token 也取 JMdict 首条读音*：会导致 `行った` 被覆成 `いく`、`額` 永远强选 `entries[0]`，破坏 UniDic 对单 token 活用的天然优势，拒绝。
  - *多条 JMdict 候选按顺序取第 1 条*：词典条目顺序不等价于上下文概率，存在误改风险，故限定“唯一去重读音”。

### Decision 3：整 Span 级振假名几何映射（保留精确逐词元像素框）
- **方案**：在 `OverlayAnnotationLayer` 与 `FuriganaLayout` 中，为 `HasOverriddenReading == true` 的 `GroupedWord` 增加一条「整 span 单色振假名」渲染分支：
  - 将 `word.SourceTokenLines` 按 `SourceTokens` 累积偏移展平为 span 级行段列表 `SpanLineSlice(LineIndex, SpanSurfaceOffset, SurfaceLength, Bounds)`（不合并不同 token 的框，保留每个源 token 的精确像素边界，跨行词天然保留每行独立切片）。
  - 对 `(word.Surface, word.Reading)` 调用既有的 `FuriganaSettings.GetFuriganaSegments`。例如：
    - `("頭を垂れる", "こうべをたれる")` → `[頭=>こうべ] + [を=>(kana)] + [垂=>た] + [れる=>(kana)]`；
    - `("頭を垂れた", "こうべをたれた")` → `[頭=>こうべ] + [を=>(kana)] + [垂=>た] + [れた=>(kana)]`；
    - `("踵を返して", "きびすをかえして")` → `[踵=>きびす] + [を=>(kana)] + [返=>かえ] + [して=>(kana)]`；
    - `("一週間", "いっしゅうかん")` → `[一週間=>いっしゅうかん]`。
  - 对每个含汉字的 `FuriganaSegment`，按 `[SurfaceStart, SurfaceStart + SurfaceLength)` 与展平后的 `SpanLineSlice` 求交并按行求并集框：
    - 单个汉字段 `頭`（`[0,1)`）精准落在源 token `頭` 的 OCR 像素框正上方；
    - 跨多 token 连续汉字段 `一週間`（`[0,3)`）在同一行上合并 `一` 与 `週間` 的像素框居中绘制 `いっしゅうかん`，若跨两行则按各行覆盖字符比例分拍绘制。
  - 纯假名/送假名段由于音调已抑制，不绘制文字也不绘制音调圆点。
- **备选方案与取舍**：
  - *直接用整个 `word.Bounds` 按字符数等比切片*：在含半角、标点或不等宽假名/汉字字体时位置容易漂移；复用 `SourceTokenLines` 的源 token 精确框既简单又对齐精准。

### Decision 4：LLM 按需读音纠偏 Schema、校验与同句多实例绑定
- **方案**：
  1. **Schema 与三协议一致性**：在 `PhraseGroupSchema.Root` 的 `words.items.properties` 中新增必填字符串字段 `"reading": { "type": "string" }`（并加入 `required` 数组），与既有的 `translation` 字段采用完全相同的严格 Schema 惯例（空字符串 `""` 表示本地表内读音已正确，无需覆盖；仅当语境读音确实与本地读音不同时填写完整表记对应的平假名读音，如 `ひたい`、`いっしゅうかん`、`こうべをたれる`）。OpenAI Chat Completions 示例 JSON 同步加入 `"reading":""`；Anthropic Tool 与 OpenAI Responses 自动继承 `PhraseGroupSchema`。
  2. **Prompt 约束**：在 `PhrasePromptBuilder.WordsInstruction` 中清晰指导：仅在本地提供的读音在当前上下文中错误（如多音字语境消歧、惯用句特殊读音、数词/量词音便或熟字训）时填写 `reading`（纯平假名，覆盖整个 chunk 的 `headword` 完整读音，包含送假名）；若本地读音正确则必须返回 `""`。
  3. **独立容错校验（不连坐词义）**：在 `WordMeaningValidator` 中对 `parsed.Reading` 单独校验：
     - 空/空白 → 视为未覆盖；
     - 转为平假名后检查：长度在 `1..64` 内、全部字符为合法假名（平假名 `\u3041-\u3096`、`ー`、`ゝ`、`ゞ`）、且经平假名归一后与本地 `span.Reading` 确实不同；
     - 若 `span.Surface` 含汉字但 `reading` 含汉字/拉丁字母/标点/空格（非法值），**仅丢弃该 `reading` 覆盖并记录一条诊断警告**，保留该词合法的 `pos`/`meaning`/`grammar`；
     - 若 `span.Surface` 为纯假名（不含汉字），忽略 `reading` 覆盖，防止模型乱改助词/纯假名词读音。
  4. **同句同表记多处出现的有序绑定**：
     - `LocalSpanSummary`（第 5 层 `PhraseAnalysis`）增加 `SpanIndex`（该句段内 `0..N-1` 的顺序编号）；`GroupedWord`（第 5 层 `WordGrouping`）同样记录 `SegmentSpanIndex`（同一 `SentenceSegment` 内由同一 `ITokenSpanResolver` 产出，索引天然一一对应）。
     - `WordMeaningValidator` 将 `localSpans` 按 `Surface` 分组为有序队列 `Dictionary<string, Queue<LocalSpanSummary>>`：
       - 当 `localSpans` 只有 1 个 `額` 而模型返回 2 个 `額` 时，第 1 个出队绑定，第 2 个队列为空 → 仍按“重复注释”丢弃（100% 兼容既有单次去重场景）；
       - 当 `localSpans` 在同一句中有 2 个 `額`（索引 0 与 5）时，模型按表顺序返回的两个 `額` 分别绑定到索引 0（`ひたい`）和索引 5（`""` 保持 `がく`），第 3 个才作为多余重复丢弃。
     - `WordMeaningView` 记录 `SegmentId`、`SpanIndex`、`Reading`（有效读音）、`HasOverriddenReading`，并在 `HasOverriddenReading == true` 时令 `PitchAccents = []`。
  5. **两阶段发布与缓存集成（不违背 Core 分层）**：
     - `PhraseAnalysisCache` 缓存的 `PhraseAnalysisResult` 原本就存 `ParsedWordMeaning[]`，新增 `Reading` 后天然随成功句段一起缓存与跨轮复用（不需要改缓存键）。
     - 在第 7 层 `WordOverlayApplicationService` 中，无论是 `ShowPreview`（复用缓存段）还是 `AnalyzeAndPublishAsync`（本轮完成段），构造 `WordOverlaySession` 前统一将 `PhraseAnalysisRun.Words` 中带 `HasOverriddenReading == true` 的有效读音回写到对应的 `GroupedWord`（通过 `(SegmentId, SegmentSpanIndex)` 精准定位，使用 `GroupedWord.WithOverriddenReading(reading)` 返回新不可变实例）。
     - 第 6 层 `WordOverlaySession` 优先按 `(word.SegmentId, word.SegmentSpanIndex)` 查找 `WordMeaningView`（找不到再回退到旧的 `(word.Surface, word.Reading)` 键以兼容直接构造会话的旧调用方与单测）。
     - `OverlayAnnotationLayer.Matches(...)` 检查 `word.Reading` 与 `word.HasOverriddenReading`：读音未变的词直接复用原有 `Canvas` 元素，仅有读音被 LLM 修正的词（或新获得下划线的词）在同一帧局部重绘。

### Decision 5：LLM JSON 文本容错解析与回退端点内存缓存
- **JSON 容错提取**：新增共享内部辅助类 `LlmJsonExtractor`（位于 `KotobaSenpai.Platform.Windows.Llm`），供 `OpenAiResponsesProtocol.ExtractContentRoot` 与 `OpenAiChatCompletionsProtocol.ExtractContentRoot` 共同调用：
  1. 首先执行标准 `JsonDocument.Parse(rawText)`，成功则零开销返回。
  2. 失败时尝试安全修复：
     - 去除首尾空白；若整段被单个 Markdown 代码围栏（` ```json\n...\n``` ` 或 ` ```\n...\n``` `）包裹，则剥离围栏后再解析；
     - 若仍失败且首个非空白字符为 `{`，使用 `Utf8JsonReader` 完整扫描第一个平衡的根 JSON 对象（正确处理字符串内转义与引号内的花括号），记录根对象结束字节位置 `endPos`；
     - 检查 `rawText[endPos..]` 的剩余尾部：仅当剩余字符**全部仅由空白字符与多余的 `}` 组成**（例如 DeepSeek 偶发的 `...}}`）时，切片提取 `[startPos..endPos]` 并解析为合法 `JsonDocument`，同时通过 `ILogger` 输出 `Warning` 日志记录已容错恢复的尾随多余字符数；
     - 若尾部包含其他非 `}` 文本、第二个 JSON 对象，或根对象本身被截断/语法损坏，则拒绝修复，照常抛出异常走 `MalformedJson` 告警与降级流程。
  - 注意：`AnthropicMessagesProtocol` 使用 `tool_use.input`（已在 JSON 信封内解析为对象），不受文本层尾随 `}` 影响，保持原逻辑。
- **回退端点记忆**：在 `LlmPhraseAnalyzer` 中维护线程安全的内存映射 `ConcurrentDictionary<(string ProtocolId, string ConfiguredEndpoint), Uri> _resolvedEndpoints`：
  - 当 `singleEndpoint == false` 时，构造完标准 `candidates = LlmEndpointHelper.AnalysisCandidates(endpointUri, protocol.Path)` 后，若缓存命中且该 `Uri` 在候选集中，将其提前到候选列表首位（其余候选保持原有相对顺序去重）。
  - 请求返回 2xx 时，将 `acceptedEndpoint` 写入缓存（仅当 `singleEndpoint == false`）；
  - 若命中的缓存地址随后返回 HTTP 404/405，立即从缓存移除该键并继续顺次尝试其余候选；
  - 用户更改协议或端点配置时，复合键自然未命中，无需手动失效；进程重启后清空，无持久化脏数据风险。

## Risks / Trade-offs

- **[Risk] 发布包体积增加 ~50 MB（未压缩 `jmdict.db`；Inno Setup LZMA2 压缩后约增 ~12–18 MB）** → **Mitigation**：当前安装包已约 613 MB（含 .NET 自包含运行时 + MeikiOCR ONNX + 离线 UniDic），`jmdict.db` 压缩后占比不到 3%，换取全部安装用户开箱即用的多词词典合并与读音回流，收益远大于体积增量；后续如有需要可另行评估剥离 `senses` 字段生成轻量读音+词表索引。
- **[Risk] LLM 偶尔把正确的本地读音也误改成错误读音（幻觉纠偏）** → **Mitigation**：Prompt 明确要求“仅当上下文中本地读音确有错误时填写，否则必须输出 `""`”；校验器强制要求纯假名、长度受限、忽略纯假名词覆盖；保留本地预览先出、诊断文件记录覆盖前后读音，便于观测与微调 Prompt。
- **[Risk] 严格 JSON Schema 下旧缓存或个别第三方 OpenAI 兼容模型忽略新字段 `reading`** → **Mitigation**：`PhraseResponseParser.ParseWords` 将 `reading` 视为可缺省字段（缺失或非字符串时视作 `null` 即不覆盖，不导致整段 `MalformedJson`）；新字段默认值 `""` 几乎不增加输出 token 成本（每词约 2–3 token）。
- **[Risk] 活用多词 JMdict 匹配误合非惯用句序列** → **Mitigation**：严格限定必须 `Tokens.Count > 1`、含至少一个汉字、且末尾仅在 `動詞`/`形容詞` 上尝试辞书形归一化；多词 `kanji` 命中要求唯一去重读音才覆盖读音，避免误伤。
- **[Risk] 同句内两个相同表记词，若 LLM 漏输出其中一个导致顺序错位** → **Mitigation**：仅对 `headword` 匹配的词按队列依次消费；若长度不足，末尾未匹配词安全回退本地读音与无释义提示，不影响其余不同表记词的匹配（不发生跨词级联错位）。
- **[Risk] `OpenAiResponsesProtocol` 构造函数与日志注入** → **Mitigation**：`LlmJsonExtractor` 保持纯静态无状态方法，通过 `out string? recoveryNote` 把容错恢复说明回传给调用方 `LlmPhraseAnalyzer`，由已有 `_logger` 统一记录 Warning，不改变 `ILlmProtocol` 既有构造签名。

## Migration Plan

1. 开发/发布环境运行 `scripts/Prepare-Jmdict.ps1` 生成/校验 `src/KotobaSenpai.App/Assets/Dictionary/jmdict.db`（并将其加入 `.gitignore` 避免大二进制入库）。
2. 按分层顺序实施：Core（`Dictionary` → `WordGrouping` / `PhraseAnalysis` → `Overlay` → `Workflow`）→ `Platform.Windows`（LLM 协议、容错解析、端点记忆）→ `Presentation.Wpf`（整 span 振假名渲染与弹窗）→ `App`（诊断日志与打包脚本）。
3. 完整运行 `dotnet test`（包含架构测试 `KotobaSenpai.Architecture.Tests`、核心/平台/应用单测），并执行真实回归验证（`額に汗をかく`、`支払う額`、`頭を垂れる`、`頭を垂れた`、`踵を返す`、`踵を返して`、`目の当たり`、`一週間` 以及模拟尾随 `}` 响应）。
4. 若需回滚，本变更不涉及磁盘持久化格式或 `settings.json` 结构破坏性修改，直接回退代码即可无缝恢复。

## Open Questions

- 无阻塞性未决问题。关于 `jmdict.db` 体积优化（如仅保留 `kanji`/`reading`/`entries.reading`、去除弹窗已不再使用的英文 `senses`），当前 `JmdictSqliteRepository` 与既有测试仍读取 `senses` 列，本次保持表结构兼容不破坏既有契约，作为后续可选瘦身项记录。
