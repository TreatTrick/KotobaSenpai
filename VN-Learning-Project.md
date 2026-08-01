# VN-Learning Windows MVP1 项目实施计划

> **给 Agentic Worker：** 实施本计划时，建议使用 `superpowers:subagent-driven-development` 或 `superpowers:executing-plans`，按任务逐项完成。每个任务使用复选框跟踪。

**目标：** 构建一个 Windows 桌面日语学习伴侣：用户在视觉小说或 galgame 中按下快捷键，应用截取当前台词，完成日语分词、固定搭配识别、振假名、上下文词义解释和例句生成。

**架构：** 采用本地优先的 WPF 桌面应用。窗口捕获、OCR、分词、振假名、结果展示和缓存都在本地完成；只把清洗后的日文文本、候选词块和上下文发送给 DeepSeek V4 Flash，模型返回结构化的词义和语法解释。MVP1 不部署服务器，用户通过 BYOK 提供自己的 API Key。Windows 平台能力通过接口隔离，便于以后增加 macOS。

**技术栈：** C# / .NET 10 LTS、WPF、`Windows.Graphics.Capture`、`Windows.Media.Ocr`、Win32 `RegisterHotKey`、`NMeCab` + 日语词典、`HttpClient` + `System.Text.Json`、DeepSeek OpenAI-compatible API、SQLite、Dapper、`CommunityToolkit.Mvvm`、xUnit、Inno Setup 或 Velopack。

## 全局约束

- MVP1 只支持 Windows 10/11 桌面应用。
- 优先支持窗口化和无边框窗口；独占全屏只做尽力支持，并提供手动输入/手动框选回退。
- MVP1 使用 BYOK，不做账号、订阅、支付、托管 Token 服务器。
- DeepSeek V4 Flash 接收 OCR 后的文本，不默认接收游戏截图。
- MVP1 不做 Anki、长期句子/单词收藏、复习系统、云同步、TTS、社交功能和移动端。
- H Scene 可以在供应商接受的前提下进行语言分析；不绕过供应商审核，并且要有拒答/超时后的本地回退。
- 不向远程服务器保存截图或原始游戏台词；本地缓存必须可清除。
- API Key 必须使用 Windows Credential Manager 或 DPAPI 保存，禁止明文写入配置文件。
- 用户界面以简体中文为主；日文原文、代码、API 名称和日志字段保留原文。
- 所有模型响应必须经过版本化 JSON Schema 校验后才能显示。
- 不宣传“支持所有游戏”，上线时发布经过验证的游戏/引擎矩阵。

---

## 1. 产品定义

### 1.1 用户问题

日语学习者玩视觉小说时，常常能认识部分单词，却不知道哪些词组成固定搭配，也无法判断多义词在当前句子中的准确含义。切换浏览器词典或翻译器会打断游戏，而且通常丢失上下文。

### 1.2 产品承诺

用户在游戏中按一次快捷键，VN-Learning 就把当前台词解析成可交互的语言解释：词语和固定搭配被标出，汉字显示振假名，点击后可以看到当前语境下的词义、语法作用和简短例句。

### 1.3 目标用户

- 在 Windows 上玩日语视觉小说的中文母语日语学习者。
- 能看懂部分日文，但经常被固定搭配、语法和多义词卡住的中级学习者。
- 能够配置 DeepSeek API Key 的技术型用户。

### 1.4 后续用户群

- 英语母语日语学习者。
- 使用日式 RPG、字幕、漫画等日文桌面内容的用户。
- 愿意贡献游戏配置和词块规则的开源社区成员。

### 1.5 核心差异化

产品不是普通翻译器，也不是只做 OCR 查词的词典。核心输出是根据上下文排序的词块，包括非连续结构，例如：

- `〜ないことには〜ない`
- `〜たり〜たりする`
- `〜わけではない`
- 被其他成分分隔开的动词、助词和补助动词组合

每个词块都要说明当前语境下的含义，以及为什么不是其他词典义项。

### 1.6 市场与商业判断

加入 Anki 作为后续阶段，并且已经验证 DeepSeek 可以分析 H Scene 后，产品概念评分约为 **7.5/10**。达到 8 分以上的关键不是继续堆功能，而是做到：

- 词块边界准确；
- 多义词消歧准确；
- 端到端延迟低；
- 在真实游戏中稳定；
- 用户愿意反复使用。

公开竞品已经验证需求，但没有明显占据“自动识别非连续词块 + 按上下文解释”的完整交互：

- Game2Text：免费 OCR、Hook 和查词工作流，仓库约 291 Stars。
- GameSentenceMiner：游戏 OCR、Yomitan 悬浮查词、振假名、翻译和 Anki，约 745 Stars。
- meikipop、Kaku：桌面 OCR 词典，约 472 和 238 Stars。
- DokiDokiDict：AI 释义排序和振假名，约 1 Star。
- Yomiko：视觉小说窗口捕获、逐行视觉模型分析和悬浮词典，约 1 Star。
- Yomitan、Textractor、LunaTranslator 是更成熟的底层工具，但重点分别是词典、文本 Hook 和翻译。

Stars 只是开发者关注信号，不等于活跃用户或付费用户。产品必须通过真实台词演示，证明它比普通词典更有用。

DeepSeek V4 Flash 当前价格大致为：未命中缓存输入 ¥1/百万 tokens，命中缓存输入 ¥0.02/百万 tokens，输出 ¥2/百万 tokens。一个约 2,000 输入、1,000 输出的文本分析约 ¥0.004，不含重试、服务器、支付和客服。因此 MVP1 使用 BYOK，不提前做服务器；未来托管模式卖的是一键配置、额度、稳定性和维护，不是 Token 原价。

没有公开的一手数据能够直接证明 galgame 用户愿意为这个具体功能付费。相邻的日语学习产品证明重度学习者会为高级体验付费，但 Game2Text 的免费模式也形成了免费价格锚点，必须通过真实测试验证。

### 1.7 平台策略

MVP1 只支持 Windows。目标游戏生态和现有工具都更偏 Windows，且 Windows 的窗口捕获、全局热键、OCR 和悬浮窗路径最直接。

macOS 作为第二平台，只有在测试用户中出现明确需求后再做；Linux 暂缓，因为 X11/Wayland、PipeWire、桌面权限、打包和 Proton/Wine 会带来更高的长期维护成本。

代码中从第一天隔离 Core 接口，但 MVP1 不为了跨平台引入 Avalonia、Electron 或第二套原生窗口实现。

### 1.8 MVP1 非目标

- 不做完整课程和 JLPT 学习系统。
- 不做 Anki 导出；Anki 放入 MVP2。
- 不做长期句子/单词收藏、SRS 复习和云同步。
- 不做托管 API、账号、支付、订阅和充值。
- 不做连续实时 OCR 或整屏自动翻译。
- 不做进程注入和文本 Hook 作为默认路径。
- 不做 TTS、音频捕获、社交功能、手机端、macOS/Linux 客户端。

## 2. MVP1 功能范围

### 2.1 用户流程

1. 用户启动应用并配置 DeepSeek API Key。
2. 用户选择游戏窗口，也可以直接粘贴或输入日文。
3. 用户按可配置的全局快捷键。
4. 应用截取窗口或文字区域，并进行本地日语 OCR。
5. 用户可以修改 OCR 结果。
6. 应用完成本地分词和振假名。
7. 应用把文本、Token 和固定搭配候选发送给 DeepSeek V4 Flash。
8. 应用校验 JSON 后显示词语、词块、含义、语法作用、例句和置信度。
9. 用户点击词语或词块查看详情，再次按快捷键或 Escape 收起。
10. 网络失败、模型拒答或超时时，应用回退到本地分词、振假名和词典结果。

### 2.2 必须实现的功能

#### 输入和捕获

- 选择并记住一个目标窗口。
- 按需截取窗口或配置的台词区域。
- 支持手动输入和 OCR 文本编辑。
- 目标窗口最小化、独占全屏或捕获失败时给出明确提示。
- 支持手动框选文字区域。

#### 分词和振假名

- 输出词面、词元、读音、词性、字符起止位置和可选屏幕坐标。
- 在汉字上方显示振假名，出现振假名时不能导致句子布局跳动。
- 保留标点、重复假名、Emoji 和游戏口语感叹词。
- OCR 引擎提供置信度时标出不确定字符。

#### 固定搭配和上下文解释

- 根据本地词典和语法规则生成连续、非连续和嵌套候选词块。
- LLM 负责候选排序、当前词义、语法作用和例句，不负责无约束地从零分词。
- 解释当前语境下的中文含义。
- 显示固定搭配的语法作用。
- 生成一到两个简短例句，并标记为 AI 生成。
- 给出置信度和不确定原因。

#### 交互

- 点击任意词语或词块显示详情面板。
- 非连续词块的所有部分同时高亮。
- 关闭分析面板后不影响游戏操作。
- 使用相同快捷键或 Escape 收起分析。
- 支持键盘在词语和词块之间移动。
- OCR 或模型失败时显示编辑和重试入口。

#### H Scene

- 对供应商接受的成人文本提供正常语言分析。
- 不上传涉及未成年人、年龄不明确的性行为、性暴力或强迫内容。
- 不通过拆分、混淆或改写请求规避供应商审核。
- 拒答、超时或分类不确定时使用本地回退。
- 默认不远程保存 H Scene 截图、原文和响应。

### 2.3 MVP1 明确不做

MVP1 不生成 Anki 卡片，不保存长期学习记录，不提供整句精修翻译，不提供托管 Token。MVP1 的唯一目标是验证“当前台词的词块和语义分析是否足够准确、足够快、足够值得使用”。

## 3. 输出数据契约

模型响应使用版本化 JSON，基本结构如下：

```json
{
  "schemaVersion": 1,
  "sourceText": "彼が来ないことには、始められない。",
  "tokens": [
    {
      "id": "t0",
      "surface": "彼",
      "lemma": "彼",
      "reading": "かれ",
      "partOfSpeech": "pronoun",
      "start": 0,
      "length": 1,
      "source": "local"
    }
  ],
  "phrases": [
    {
      "id": "p0",
      "tokenIds": ["t3", "t4", "t7", "t8"],
      "surface": "ないことには...ない",
      "kind": "grammar_pattern",
      "meaningZh": "如果不……就不能……",
      "grammarZh": "表示必要条件，后项通常为否定或不可能实现的结果。",
      "confidence": 0.92,
      "uncertainty": null,
      "source": "llm"
    }
  ],
  "examples": [
    {
      "japanese": "準備しないことには、出発できない。",
      "chinese": "如果不准备，就无法出发。",
      "generated": true
    }
  ],
  "warnings": []
}
```

客户端必须拒绝无效 Token ID、越界 Span、未知 Schema 版本、超长例句和无法解析的响应。

## 4. 架构与目录结构

```text
src/
  VnLearning.App/                  WPF 界面、ViewModel、命令、启动入口
  VnLearning.Core/                 领域模型、用例、接口、校验
  VnLearning.Platform.Windows/     窗口捕获、OCR、快捷键、DPI、悬浮窗
  VnLearning.Infrastructure/       DeepSeek、词典、SQLite、设置、缓存
tests/
  VnLearning.Core.Tests/            领域、词块、JSON 和校验测试
  VnLearning.Infrastructure.Tests/ API Mock、缓存、词典和设置测试
  VnLearning.Platform.Windows.Tests/ 坐标、窗口和平台适配测试
testdata/
  japanese-gold/                   人工标注的日语句子和正确词块
docs/
  decisions/                       架构决策和测试报告
```

### 4.1 Core 模块

Core 不引用 WPF 或 Windows API，定义：

- `Token`
- `PhraseCandidate`
- `PhraseExplanation`
- `ExampleSentence`
- `AnalysisRequest`
- `AnalysisResult`
- `ITextAnalyzer`
- `IOcrEngine`
- `IWindowCapture`
- `IHotkeyService`
- `ILlmProvider`
- `IResultCache`

核心接口：

```csharp
public interface ITextAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(
        AnalysisRequest request,
        CancellationToken cancellationToken = default);
}
```

### 4.2 Windows 平台模块

负责目标窗口选择、`Windows.Graphics.Capture`、`Windows.Media.Ocr`、`RegisterHotKey`、DPI 坐标变换和透明置顶悬浮窗。

### 4.3 基础设施模块

负责 `NMeCab`、日语词典、振假名、固定搭配规则、DeepSeek 请求、JSON 校验、SQLite 缓存、DPAPI/Credential Manager 和脱敏日志。

### 4.4 应用模块

负责 MVVM 状态、快捷键命令、分析/重试/收起流程、设置窗口、详情面板和所有用户可见错误。

### 4.5 数据流

```text
快捷键
  -> WindowCapture
  -> OcrEngine
  -> 可编辑 OCR 文本
  -> 本地分词/振假名/候选词块
  -> ResultCache
  -> DeepSeekProvider
  -> JSON 校验
  -> AnalysisResult
  -> WPF 悬浮窗和详情面板
```

## 5. 技术选型决策

### 5.1 WPF，而不是 WinUI 3

MVP 选择 WPF，因为透明悬浮窗、Win32 互操作、全局快捷键、点击穿透和调试路径更成熟。WinUI 3 界面更现代，但不能提高词块分析准确率，且会增加窗口和原生 API 适配成本。

### 5.2 本地 OCR，再调用文本模型

先使用 `Windows.Media.Ocr`，并检测日语 OCR 语言包。通过 `IOcrEngine` 隔离实现，以便在视觉小说字体表现不佳时替换为 ONNX/Manga OCR。不要把截图直接发送给 DeepSeek V4 Flash，当前官方 API 文档没有确认图片输入和图片计费规则。

### 5.3 本地候选，再让 LLM 排序

先通过 MeCab、词典和语法规则生成 Token 与词块候选，再让 DeepSeek 判断当前词义、词块含义和语法作用。这样可以降低幻觉、减少 Token、提高测试可重复性。

### 5.4 本地缓存和安全存储

SQLite 只保存设置和可清除的分析缓存。缓存键由规范化文本、上下文、模型 ID、Schema 版本和分析器版本组成。API Key 使用 Windows Credential Manager 或 DPAPI 保存。

### 5.5 Provider 适配层

实现 `ILlmProvider` 和 `DeepSeekProvider`，但不把业务逻辑绑定到 DeepSeek。未来可以添加 Ollama、本地模型或其他 OpenAI-compatible 服务。

## 6. 质量指标和 Go/No-Go

### 6.1 技术质量目标

- 第一版建立至少 100 条人工标注日语游戏台词评测集。
- 词块边界准确率达到 85% 以上。
- 多义词首选词义准确率达到 90% 左右。
- 从快捷键到首个可用结果的 P95 不超过 2.5 秒。
- 在明确支持的窗口化/无边框游戏中，OCR 成功率达到 90% 以上。
- 自动测试中不允许出现未处理的 JSON 校验异常。

### 6.2 用户验证目标

- 找 20～30 名日语学习者测试 5～10 个真实视觉小说。
- 至少 50% 的测试者在 7 天内分三次以上使用分析流程。
- 至少 70% 的已点击词块解释被评价为有帮助。
- 至少 5 名测试者在 BYOK 体验后主动要求“一键托管版本”。
- OCR 或模型失败不能导致用户重启游戏或重启应用。

### 6.3 停止或调整条件

如果词块准确率、语义准确率、延迟或重复使用率不达标，暂停开发 Anki、充值、服务器和跨平台功能，先修正分析策略。

## 7. 可执行开发任务

### 任务 0：创建解决方案和测试集

**创建文件：**

- `VnLearning.sln`
- `src/VnLearning.Core/VnLearning.Core.csproj`
- `src/VnLearning.App/VnLearning.App.csproj`
- `src/VnLearning.Platform.Windows/VnLearning.Platform.Windows.csproj`
- `src/VnLearning.Infrastructure/VnLearning.Infrastructure.csproj`
- `tests/VnLearning.Core.Tests/VnLearning.Core.Tests.csproj`
- `tests/VnLearning.Infrastructure.Tests/VnLearning.Infrastructure.Tests.csproj`
- `testdata/japanese-gold/README.md`

**执行步骤：**

- [ ] 创建 .NET 解决方案和项目引用，确保依赖方向只从 App/Platform/Infrastructure 指向 Core。
- [ ] 开启 nullable reference types、隐式 using、编译分析器和 Release 构建。
- [ ] 配置无需 API Key 即可运行的 `dotnet test`。
- [ ] 加入 20 条人工标注测试台词，覆盖多义词、普通词组和至少三种非连续语法结构。
- [ ] 执行 `dotnet build` 和 `dotnet test`，全部通过后再开始界面工作。

### 任务 1：定义领域模型和 JSON 契约

**创建文件：**

- `src/VnLearning.Core/Models/Token.cs`
- `src/VnLearning.Core/Models/PhraseCandidate.cs`
- `src/VnLearning.Core/Models/AnalysisRequest.cs`
- `src/VnLearning.Core/Models/AnalysisResult.cs`
- `src/VnLearning.Core/Contracts/AnalysisJsonSchema.cs`
- `src/VnLearning.Core/Services/ITextAnalyzer.cs`
- `tests/VnLearning.Core.Tests/AnalysisContractTests.cs`

**执行步骤：**

- [ ] 使用不可变 record 定义 Token、Phrase、Example、Warning 和 AnalysisResult。
- [ ] 实现 Span 校验，确保每个词块引用存在的 Token 且没有越界。
- [ ] 实现未知 Schema 版本拒绝逻辑。
- [ ] 测试连续词块、非连续词块、嵌套词块、重复 Token ID、非法例句和空响应。
- [ ] 执行 `dotnet test tests/VnLearning.Core.Tests -v minimal`。

### 任务 2：实现日语分词和振假名

**创建文件：**

- `src/VnLearning.Infrastructure/Japanese/NMeCabTokenizer.cs`
- `src/VnLearning.Infrastructure/Japanese/FuriganaResolver.cs`
- `src/VnLearning.Infrastructure/Japanese/DictionaryLoader.cs`
- `tests/VnLearning.Infrastructure.Tests/JapaneseAnalysisTests.cs`
- `THIRD-PARTY-NOTICES.md`

**执行步骤：**

- [ ] 引入有明确许可证的日语词典，并在 `THIRD-PARTY-NOTICES.md` 记录来源和许可证。
- [ ] 实现离线 Token 化，输出词面、词元、读音、词性和字符偏移。
- [ ] 优先从本地词典解析振假名，仅在本地无法消歧时调用模型。
- [ ] 保留标点、重复假名、Emoji 和游戏感叹词。
- [ ] 测试汉字复合词、动词变形、纯假名词、标点和歧义读音。
- [ ] 在 100 条句子上测量本地处理延迟，目标是每句低于 100 ms。

### 任务 3：实现固定搭配候选生成

**创建文件：**

- `src/VnLearning.Infrastructure/Japanese/GrammarPatternCatalog.cs`
- `src/VnLearning.Infrastructure/Japanese/PhraseCandidateGenerator.cs`
- `tests/VnLearning.Core.Tests/PhraseCandidateTests.cs`
- `testdata/japanese-gold/phrase-candidates.json`

**执行步骤：**

- [ ] 建立版本化常见语法和固定表达目录。
- [ ] 根据词典和相邻 Token 生成连续词块。
- [ ] 根据显式规则生成 `〜ないことには〜ない`、`〜たり〜たりする`、`〜わけではない` 等非连续词块。
- [ ] 允许词块重叠和嵌套，但限制单句最大候选数。
- [ ] 为候选记录来源：`dictionary`、`grammar-rule` 或 `heuristic`。
- [ ] 使用黄金测试集验证 Token ID 和 Span。

### 任务 4：实现 DeepSeek Provider 和分析编排

**创建文件：**

- `src/VnLearning.Infrastructure/Llm/DeepSeekProvider.cs`
- `src/VnLearning.Infrastructure/Llm/DeepSeekRequestBuilder.cs`
- `src/VnLearning.Infrastructure/Llm/AnalysisResponseValidator.cs`
- `src/VnLearning.Core/Services/TextAnalysisOrchestrator.cs`
- `tests/VnLearning.Infrastructure.Tests/DeepSeekProviderTests.cs`
- `tests/VnLearning.Core.Tests/TextAnalysisOrchestratorTests.cs`

**执行步骤：**

- [ ] 使用 DeepSeek OpenAI-compatible API 和 `deepseek-v4-flash`。
- [ ] 使用稳定系统提示词，要求只返回词块 Span、当前词义、语法作用、置信度和短例句。
- [ ] 把稳定 Schema 和提示词放在动态台词之前，以便使用前缀缓存。
- [ ] 默认关闭 thinking，配置超时、取消、最大输出长度和重试次数。
- [ ] 日志中脱敏 API Key 和原始台词。
- [ ] 通过 Core Schema 校验后才返回 UI。
- [ ] 将限流、非法 JSON、拒答、超时和网络异常转为明确的 Warning。
- [ ] 使用 Fake Provider 做自动化测试，CI 不依赖真实 API Key。

### 任务 5：实现本地设置、Key 存储和缓存

**创建文件：**

- `src/VnLearning.Infrastructure/Storage/SqliteConnectionFactory.cs`
- `src/VnLearning.Infrastructure/Storage/ResultCache.cs`
- `src/VnLearning.Infrastructure/Security/ApiKeyStore.cs`
- `src/VnLearning.Infrastructure/Configuration/AppSettings.cs`
- `tests/VnLearning.Infrastructure.Tests/StorageTests.cs`

**执行步骤：**

- [ ] 创建 SQLite 设置和分析缓存表。
- [ ] 用规范化文本、上下文、模型 ID、Schema 版本和分析器版本生成缓存键。
- [ ] 禁止缓存截图字节。
- [ ] 使用 Credential Manager 或 DPAPI 存 Key，UI 只显示已配置/未配置状态。
- [ ] 测试命中、未命中、过期、清空和模型版本失效。
- [ ] 为未来数据库变更设置迁移版本。

### 任务 6：创建 WPF 文本分析工作台

**创建文件：**

- `src/VnLearning.App/App.xaml`
- `src/VnLearning.App/App.xaml.cs`
- `src/VnLearning.App/ViewModels/MainViewModel.cs`
- `src/VnLearning.App/Views/MainWindow.xaml`
- `src/VnLearning.App/Views/AnalysisPanel.xaml`
- `src/VnLearning.App/Views/SettingsWindow.xaml`
- `tests/VnLearning.App.Tests/MainViewModelTests.cs`

**执行步骤：**

- [ ] 创建文本输入、分析、取消、重试、清空和设置命令。
- [ ] 渲染 Token 和词块，振假名出现时保持稳定布局。
- [ ] 非连续词块的所有部分使用同一个高亮 ID。
- [ ] 详情面板显示当前含义、语法、例句、置信度和警告。
- [ ] 添加空状态、加载、Key 缺失、超时、拒答、OCR 失败和非法响应状态。
- [ ] 实现键盘导航和 Escape 关闭。
- [ ] 用 Fake Core 服务测试 ViewModel 状态变化和取消操作。

### 任务 7：增加 Windows 捕获、OCR、快捷键和悬浮窗

**创建文件：**

- `src/VnLearning.Platform.Windows/Capture/GraphicsCaptureService.cs`
- `src/VnLearning.Platform.Windows/Ocr/WindowsOcrEngine.cs`
- `src/VnLearning.Platform.Windows/Input/GlobalHotkeyService.cs`
- `src/VnLearning.Platform.Windows/Overlay/OverlayWindowService.cs`
- `src/VnLearning.Platform.Windows/WindowsPlatformModule.cs`
- `tests/VnLearning.Platform.Windows.Tests/CoordinateTransformTests.cs`

**执行步骤：**

- [ ] 实现目标窗口选择，并只在本地保存窗口句柄。
- [ ] 使用 `Windows.Graphics.Capture` 按需获取单帧。
- [ ] 检测日语 OCR 语言包，并在缺失时显示安装说明。
- [ ] 把 OCR 行/词坐标转换成 Core Token 坐标。
- [ ] 使用 `RegisterHotKey` 注册可配置快捷键，并显示冲突提示。
- [ ] 创建透明置顶悬浮窗，关闭分析时启用点击穿透。
- [ ] 打开详情时切换为可交互模式，关闭后恢复点击穿透。
- [ ] 测试 100%、125%、150% DPI 的坐标变换。
- [ ] 在至少三个真实视觉小说中测试窗口化和无边框模式。

### 任务 8：串联完整 MVP1 流程

**创建文件：**

- `src/VnLearning.App/Services/AnalysisWorkflow.cs`
- `tests/VnLearning.App.Tests/AnalysisWorkflowTests.cs`

**执行步骤：**

- [ ] 连接快捷键、捕获、OCR、文本编辑、分词、缓存、DeepSeek、校验和悬浮窗。
- [ ] 新分析开始或用户按 Escape 时取消正在运行的 OCR/LLM 请求。
- [ ] 网络或模型失败时返回最好的本地结果。
- [ ] 防止同一规范化句子的重复并发分析。
- [ ] 在 debug 面板显示 OCR、本地分析、网络请求、校验和总耗时。
- [ ] 测试成功、OCR 失败、语言包缺失、超时、拒答、非法 JSON、缓存命中和取消。

### 任务 9：打包并执行 Alpha 测试

**创建文件：**

- `README.md`
- `docs/user-guide/windows-setup.md`
- `docs/user-guide/api-key-privacy.md`
- `docs/test-report/mvp1-evaluation.md`
- `installer/VnLearning.iss`

**执行步骤：**

- [ ] 发布自包含 Windows 安装包和首次启动配置流程。
- [ ] 说明日语 OCR 语言包、窗口化要求、Key 存储和本地/云端数据边界。
- [ ] 发布已测试游戏/引擎矩阵和已知不支持模式。
- [ ] 执行 100 条黄金台词评测，记录准确率、延迟和 Schema 失败率。
- [ ] 邀请 20～30 名目标用户测试 5～10 个真实视觉小说。
- [ ] 默认不上传原始截图和台词，用户反馈只记录脱敏指标。
- [ ] 在开始 Anki、托管 Token 或跨平台开发前记录 Go/No-Go 结论。

## 8. 时间计划

### 全职、AI 辅助开发

- 第 1 周：解决方案、数据契约、文本工作台、DeepSeek Adapter、第一版黄金测试集。
- 第 2 周：分词、振假名、词块候选、JSON 校验、缓存。
- 第 3 周：WPF 界面、设置、Key 存储、手动文本分析流程。
- 第 4 周：Windows 捕获、OCR、快捷键、悬浮窗和 DPI 修复。
- 第 5～6 周：完整串联、重试、回退、真实游戏测试和打包。
- 第 7～8 周：准确率调优、用户 Alpha、文档和发布修复。

AI 辅助下，2～3 周可以做出演示版本，5～8 周可以做出真实玩家可测试的 MVP1。业余时间开发预计 8～12 周。

## 9. 风险与应对

| 风险 | 严重度 | 应对方案 |
|---|---|---|
| 风格化或竖排字体 OCR 失败 | 高 | 窗口/区域捕获、图像预处理、可编辑 OCR、可替换 OCR 接口 |
| 模型错误合并词块或选错词义 | 严重 | 本地候选、黄金评测集、Span 校验、置信度、本地回退 |
| DeepSeek 延迟、拒答或不可用 | 高 | 默认 non-thinking、超时/取消、缓存、本地词法回退 |
| 独占全屏无法捕获 | 高 | 明确支持窗口化/无边框，提供手动输入和手动框选 |
| API Key 泄露 | 严重 | DPAPI/Credential Manager、日志脱敏、禁止进入仓库和崩溃报告 |
| H Scene 供应商策略变化 | 高 | BYOK、Provider 适配层、本地回退、不规避审核、版本回归测试 |
| 游戏文本和截图版权/隐私风险 | 高 | 尽量本地处理，不远程保存，不再分发游戏素材 |
| 过早扩展到学习平台 | 高 | MVP1 排除 Anki、服务器、支付、统计和跨平台 |
| Windows-only 限制传播 | 中 | Core 保持平台无关，先做深 Windows，再按真实需求移植 |

## 10. 后续路线

### MVP2：Anki 和学习输出

- 一键 AnkiConnect 导出。
- 卡片字段包括原句、关注词块、振假名、当前语义、语法解释、例句和可选截图/音频。
- 本地句子/词块历史和删除/导出。
- 使用前 1～3 句作为会话上下文，但不上传长期历史。

### MVP3：托管 Token

- 可选账号和额度服务。
- 服务端调用 DeepSeek，提供严格的每日/月度额度。
- 支付、退款、限流、滥用监控和模型健康状态。
- 默认不保存截图和原始游戏台词。
- 单独设计 H Scene 同意和内容边界。

### MVP4：平台和社区扩展

- 开源 Core 和 Provider 接口。
- 社区贡献的游戏/引擎预设。
- 只有在用户需求明确后增加 macOS 的 ScreenCaptureKit、Vision 和 AppKit 适配。
- Linux 仅在有社区贡献者或明确用户规模后支持。

## 11. MVP1 完成定义

以下条件全部满足，才算 MVP1 完成：

- 新 Windows 安装可以不修改配置文件完成 DeepSeek Key 设置。
- 用户输入或粘贴一句日文，可以看到分词、振假名、上下文词义、词块解释、例句和置信度。
- 用户选择窗口并按快捷键，可以完成 OCR、修改文本和同样的分析流程。
- 非连续词块的所有部分可以一起高亮，并能映射回原始 Token。
- DeepSeek 慢、失败、非法 JSON 或拒答时，应用仍能提供本地结果。
- 正常日志不包含 API Key、截图和原始台词。
- 自动测试不依赖真实 API Key。
- 黄金评测和真实游戏 Alpha 报告已记录。
- 在开始 Anki、托管 Token 或跨平台开发前，已经记录 Go/No-Go 决策。

## 12. 交给架构和开发 AI 的启动说明

从任务 0 和任务 1 开始，不要先做 WPF 悬浮窗。第一承重风险是：数据契约能否表示非连续词块，以及 DeepSeek 是否能在黄金测试集上稳定返回有用结果。

实施时必须保持以下决策：

- MVP1 只支持 Windows。
- 第一版使用 WPF，不使用 WinUI 3 或 Electron。
- OCR 和确定性分词先在本地完成，再调用 LLM。
- 第一版只实现 DeepSeek V4 Flash Provider，但保留 `ILlmProvider` 接口。
- MVP1 不做服务器、账号、支付和 Anki。
- 本地优先，云端失败时必须有可用回退。
- 不通过 Prompt 或请求载荷规避成人内容政策。

第一个评审点是文本分析工作台。如果词块分析没有明显优于普通词典，必须先调整分析策略，不能继续投入窗口捕获、悬浮窗和打包工作。
