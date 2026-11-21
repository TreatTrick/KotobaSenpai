## Why

近期针对日语振假名经典痛点用例（多音字 `額`、固定惯用句 `頭を垂れる` / `踵を返す`、熟字训 `目の当たり`、量词促音便 `一週間`）的实测与 `diag`/`logs` 诊断暴露出四类叠加问题：

1. **发布包缺失 `jmdict.db`，且词典命中读音被丢弃**：已安装版本未随包分发 `jmdict.db`，导致多词合并失效；即便在含 `jmdict.db` 的调试构建中，`TokenBoundarySpanResolver` 已将 `頭を垂れる`、`踵を返す`、`一週間` 合并并唯一命中 JMdict 词条（读音分别为 `こうべをたれる`、`きびすをかえす`、`いっしゅうかん`），但 `LookupSpan` 仍强行拼接 UniDic 原短单位读音（`アタマヲタレル` / `カカトヲカエス` / `イチシュウカン`），渲染层也仅按 UniDic 源 token 绘制振假名。
2. **多词词典合并无法命中活用形惯用句**：真实文本中的 `頭を垂れた`、`踵を返して` 等非辞书形惯用句因字面拼接与词典原型不一致而无法合并及取音。
3. **纯语境多音字缺乏纠音通道**：UniDic 基于一阶马尔可夫（2-gram）无语义模型，在 `額に汗をかく` 等语境下仍恒输出 `がく`；现有 LLM 分析明明已对每个句段返回语义，但 Prompt 与 Schema 显式禁止模型返回或修正读音。
4. **LLM 响应容错与端点请求效率不足**：DeepSeek（`deepseek-flash` + OpenAI Responses）在 `strict: true` 下仍偶发在合法 JSON 对象末尾多输出一个 `}`，导致 `目の当たり` 整段解析失败、丢失下划线与释义弹窗；且未记住已探测成功的回退端点（如 `/v1/responses`），每个句段每次都先请求根路径吃一次 HTTP 404 再重试。

本次变更一次性解决上述 P0、P1（活用多词匹配）与 P2 问题；**按明确需求，本次不包含本地「数词 + 助数词」硬编码促音便规则层**（词典收录项由 JMdict 读音回流解决，其余未收录组合由 LLM 语境读音纠偏覆盖）。

## What Changes

- **补齐 JMdict 索引随发布打包与构建校验**：在应用发布与安装包构建流程中显式纳入 `jmdict.db` 并做存在性校验，同时保留运行时文件缺失时的降级行为。
- **支持活用形多词词典匹配**：扩展 `TokenBoundarySpanResolver`，在构造含汉字的多 token 候选时，支持将末尾可活用词元归一化为辞书形（`OrthBase`/`Lemma`）查 JMdict 并吸收后续活用助动词/接续助词（如 `頭を垂れた`、`踵を返して`）。
- **启用 JMdict 唯一读音回流与整词振假名渲染**：当含汉字的多 token span 在 JMdict 唯一确定一条词典读音（含活用尾假名还原后）且与 UniDic 拼接读音不同时，采用词典读音作为 span 有效读音；覆盖层渲染按整 span 对齐汉字与假名段，并在读音被覆盖时抑制过期的 UniDic 音调标记，避免错误高低音着色。
- **开放 LLM 按需语境读音纠偏（覆盖三协议）**：在共享 `words[]` Schema、Prompt 和三套协议解析器中增加可选读音字段 `reading`（与本地读音一致时返回空字符串 `""`，仅在语境读音与本地不同时输出正确假名）；校验通过后在既有单次分析后刷新中同步更新覆盖层振假名、悬停弹窗与短语组内词条读音，并支持同句内相同表记多处出现按序独立纠偏（如同一句中先后出现 `額(ひたい)` 与 `額(がく)`）。
- **增强 LLM 结构化 JSON 解析容错并复用已探测端点**：在文本型 JSON 响应提取阶段容忍合法单个根 JSON 对象末尾的多余 `}` 或外层代码块包裹（并记录诊断告警），避免整段误判 `MalformedJson`；在 `LlmPhraseAnalyzer` 中按 `(protocolId, configuredEndpoint)` 内存缓存最近成功的候选分析 URI，避免后续句段反复先请求 404 路径。

## Capabilities

### New Capabilities
<!-- 本次变更为既有能力的增强与缺陷修复，无新增顶层独立能力目录 -->

### Modified Capabilities
- `english-dictionary`: 要求发布构建与安装包随附 `jmdict.db` 索引文件；定义多词 JMdict 唯一命中时的词典读音回流规则；悬停弹窗读音更新为采用最终有效读音（LLM 语境纠偏 > JMdict 词典读音 > UniDic 读音）。
- `word-grouping`: 扩展 `TokenBoundarySpanResolver` 的多 token 候选匹配规则，支持末尾活用词元按辞书形命中 JMdict 惯用句并吸收完整活用链。
- `furigana-overlay`: 将振假名读音来源从“仅 UniDic”升级为“LLM 语境纠偏 > JMdict 整词读音 > UniDic 读音”；支持整 span 级跨 token/跨行假名精确对齐渲染及分析完成后的局部重绘。
- `pitch-accent-display`: 定义当词块读音被 JMdict 整词读音或 LLM 语境纠偏覆盖且偏离 UniDic 原读音时，将对应词的音调置为未知/不绘制过期高低音标记，防止张冠李戴。
- `llm-word-meanings`: 在 `words[]` 中增加按需读音纠偏字段 `reading` 及严格假名校验规则；支持同句同表记按出现顺序绑定；弹窗与短语组详情统一展示有效读音。
- `staged-overlay-analysis`: 本地预览阶段使用本地有效读音（JMdict > UniDic），云端分析完成后的单次刷新一并发布经校验的 LLM 语境读音纠偏，并在增量缓存复用时保留已纠偏读音。
- `llm-protocol-abstraction`: 三套协议共享 `words[].reading` 结构声明；文本响应提取支持安全剥离多余尾随 `}` / 代码块包裹；按配置快照维度复用已验证成功的回退分析端点 URI。

## Impact

- **Core 层**：`KotobaSenpai.Core` 中的 `LookupSpan`、`TokenBoundarySpanResolver`、`GroupedWord`、`ParsedWordMeaning`、`WordMeaningView`、`WordMeaningValidator`、`PhraseRunAssembler`、`WordOverlaySession`、`WordOverlayApplicationService`。
- **Platform.Windows 层**：`KotobaSenpai.Platform.Windows` 中的 `PhraseGroupSchema`、`PhrasePromptBuilder`、`PhraseResponseParser`、`OpenAiResponsesProtocol`、`OpenAiChatCompletionsProtocol`、`AnthropicMessagesProtocol`、`LlmPhraseAnalyzer`、`LlmEndpointHelper`。
- **Presentation.Wpf 层**：`KotobaSenpai.Presentation.Wpf` 中的 `OverlayAnnotationLayer`、`FuriganaLayout`、`PhrasePopupViewModel`、`OverlayHoverController`。
- **构建与打包**：`src/KotobaSenpai.App/KotobaSenpai.App.csproj`、`scripts/Build-Installer.ps1`、`scripts/Prepare-Jmdict.ps1`（新增构建准备脚本）。
- **诊断与测试**：`FileDiagnosticReporter` 记录最终有效读音与纠偏来源；对应 Core / Platform.Windows / App 单元测试全量补齐。
