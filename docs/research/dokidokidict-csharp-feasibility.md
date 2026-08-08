# DokiDokiDict 的 C# 复刻可行性调研

**调研日期：** 2026-07-31
**调研方法：** 基于已安装的二进制（`C:\Program Files (x86)\DokiDokiDict`）与完整源码仓库（`D:\project\DokiDokiDict`，`github.com/elwendys/DokiDokiDict`）的实证分析，辅以 GitHub API 的发布时间线。本文不依赖官方 itch.io 营销页；产品页层面的功能主张见同目录的 [DokiDokiDict 竞品快照](./dokidokidict-competitive-snapshot.md)。
**相关文档：** [VN-Learning 项目计划](../../VN-Learning-Project.md)、[竞品快照](./dokidokidict-competitive-snapshot.md)

## 结论（TL;DR）

复刻 DokiDokiDict 的**核心链路（截图 OCR → 日语分词 → 词典查词 → LLM 释义 → GUI 弹窗）在 C# 中不会因缺少 Python 库而碰壁**。关键原因是其机器学习推理（OCR、Whisper 语音转写）全部走 ONNX 模型，而 ONNX Runtime 的 C# 支持是微软官方一等公民，模型文件语言无关、可直接复用。分词（MeCab）、词典（JMdict）、LLM 调用这三项最依赖语言生态的部分，VN-Learning 已在 .NET 10 上跑通。

真正的难点是**工程量与范围，而非库的可用性**。DokiDokiDict 是一个 100+ 模块、含自建云后端、推荐器+模拟器、完整 FSRS 间隔重复、卡库 gallery、成就系统的完整平台，原作者站在 Python 库巨人肩上仍做了半年以上、至今未到 1.0。全量复刻无论用何种语言都是数月起步的工程。

C# 相对 Python 的净优势：单文件自包含发布（无需 PyInstaller 的 ~96MB 与各类打包坑）、GUI 性能（WPF/WinUI3/Avalonia 对 Qt6）、ASP.NET Core API、内存与启动速度。已投入的 .NET 代码不白费。

## 证据基础

调研从三条独立来源交叉验证：

1. **安装版二进制**：`C:\Program Files (x86)\DokiDokiDict` 为 PyInstaller onedir 打包产物（`DokiDokiDict.exe` + `_internal\` + Inno Setup 卸载器 `unins000.exe`）。`_internal` 内可见全部 Python 依赖的 `dist-info`、ONNX 模型、词典与频率数据。
2. **源码仓库**：`D:\project\DokiDokiDict`（`git remote = github.com/elwendys/DokiDokiDict`），含 `src/` 全部包、PyInstaller spec（`meikipop.win.x64.onedir.spec`）、Inno Setup 脚本（`installer_onedir.iss`）。`src/config/config.py` 声明 `APP_VERSION = "0.9.5"`，与安装版一致。
3. **可执行文件内嵌模块图**：从 exe 提取 ASCII 字符串得到全部 `src.*` 包名，与源码 `src/` 目录结构逐一对应，构成功能清单的实证依据。
4. **GitHub API**：源码仓库创建于 2026-02-25；发布仓库 `elwendys/DokiDokiDict-releases` 的公开 release 为 v0.9.3（03-09）、v0.9.4（04-17）、v0.9.5（07-27）。

源码与二进制完全对齐：`src/ocr/providers/` 下的 `glensv2 / meikiocr / owocr` 三个 provider，与安装版 `_internal\src\ocr\providers\` 是同一份文件。因此下文功能清单与可行性判断均可对照源码核验，而非从黑盒反推。

## DokiDokiDict：实证功能清单

以下按从源码模块图（`src.*` 包）归纳的子系统列出。这是实际代码组织，不是营销主张。

| 子系统 | 源码模块 | 功能 |
| --- | --- | --- |
| OCR | `src/ocr` + `providers/{glensv2,meikiocr,owocr}` + `hit_detector/hit_scan` | 三后端：①`meikiocr` 本地 ONNX（文本检测 + 识别，含竖排，为游戏优化）；②`owocr` 经 WebSocket `127.0.0.1:7331` 连外部 OCR 进程；③`glensv2` 调 Google Lens 云端（protobuf + API key）。后处理含振假名检测与纵横段落归组（`postprocessing.py`）。 |
| 音频挖矿 | `src/audio/{mining_pipeline,session_recorder,video_recorder}` | 录屏/录音 → faster_whisper 转写 → stable_whisper 词级时间戳 → 挖句入库；依赖 PyAV(FFmpeg)、PortAudio，并处理字幕（`av.subtitles`）。 |
| 分词 | fugashi + unidic（MeCab）；`src/dictionary/mecab_furiganizer` | MeCab 分词；本地注音。 |
| 词典 | `src/dictionary/*` | `jmdict_enhanced.pkl`(56MB)、Yomichan 字典格式导入、`kanji_decomposer`（拆字）、`pitch_accent`（accents.txt）、`deconjugator_v2`（活用还原）、`latin_dict`。 |
| LLM | `src/dictionary/{genai_client,gemini_ranker,gemini_reading_ranker,gemini_furiganizer}` | 使用 **Google Gemini**（`google.genai`）做注音、读序排序、义项消歧。**非以翻译为主**；无 DeepL/Claude/GPT 直连（相关字符串仅来自 sentry_sdk/transformers 内部引用）。 |
| SRS | `src/srs/*` + `src/anki/anki_connect` | 内置 **FSRS**（含 optimizer 个性化参数）+ `genanki` 生成 `.apkg` + AnkiConnect 实时同步 + `sentence_bank` 例句库。 |
| 推荐系统 | `src/recommender/*`（15 个模块） | 按词汇量与 i+1 原则对动漫/VN/LN 作品排序，含 `simulator`/`planner`/`ranking_manager` + 自建云后端（`google.cloud.storage` + FastAPI）。 |
| GUI | `src/gui/*`（40+ 模块） | PySide6/Qt6：主窗、popup、阅读器、挖矿卡库 gallery、振假名悬浮、区域选择、窗口追踪、热键、汉字 SRS、统计、成就、单词标记、Magpie 集成。 |
| 截图 | `src/screenshot/screenmanager` | 屏幕捕获。 |
| 数据 | `src/data/*` + freq_lists | anime_sub / game_script / global / literature / ln / vn 六套频率表 + jpdb 汉字频率 + kanjivg 笔画 + jouyou 汉字。 |
| 多语言 | `src/lang/{japanese,latin,latin_analyzer,latin_cognates,latin_macronizer}` | 日语 + 拉丁语（含长短音标注）。 |

## 技术栈分层

- **ML / 推理**：torch 2.8、transformers、onnxruntime 1.20、ctranslate2（faster_whisper 后端）、numba、scikit-learn、pandas、matplotlib
- **音频**：faster_whisper、stable_whisper、PyAV(FFmpeg)、PortAudio、torchaudio
- **NLP**：fugashi、unidic、tiktoken、rapidfuzz、ftfy、regex
- **GUI**：PySide6 / shiboken6（Qt6）
- **后端 / API**：FastAPI + Starlette、Flask、websockets、aiohttp、grpc/protobuf、SQLAlchemy + SQLite
- **云**：google-cloud-storage、google-genai（Gemini）、sentry_sdk、opentelemetry
- **互操作**：`pythonnet` / `clr_loader`（从 Python 桥接 .NET，推测用于调 Windows API）

## C# 复刻可行性逐项评估

> 判断基准：是否存在成熟、可维护的 C# 库或等价实现，而非"理论上能写"。难度按"已有 .NET 10 项目经验"的视角估。

| 功能 | Python 依赖 | C# 对应 | 难度 |
| --- | --- | --- | --- |
| OCR（本地 ONNX） | meikiocr → onnxruntime | **Microsoft.ML.OnnxRuntime**（官方，C# 一等公民），直接跑那几个 `.onnx` 模型 | 中（需自行实现 detect+rec 前后处理，原 Python 库已封装） |
| OCR（Google Lens） | requests + protobuf | HTTP + protobuf-net | 易 |
| OCR（owocr WebSocket） | websockets | System.Net.WebSockets | 易 |
| OCR 后处理（振假名/段落） | 纯算法 | 直接照搬 `postprocessing.py` 逻辑 | 易 |
| 屏幕截图 | mss/PIL | Windows.Graphics.Capture / Graphics.CopyFromScreen | 易 |
| Whisper 转写 | faster_whisper(CTranslate2) | **Whisper.net**（whisper.cpp 绑定，成熟）或直接用 ONNX Runtime 跑 Whisper | 中（生态略小，性能相当） |
| 词级时间戳 | stable_whisper | ⚠️ 无直接 C# 移植；用 whisper.net 自带 word timestamps 或自实现稳定逻辑 | 中 |
| 音频采集 | PortAudio/sounddevice | NAudio | 易 |
| MeCab 分词 | fugashi+unidic | **LibNMeCab**（VN-Learning 已在用） | ✅ 已解决 |
| JMdict 词典 | jmdict_enhanced.pkl | VN-Learning 已在用；`.pkl` 需重新导出为 JSON/SQLite | ✅ 已解决 |
| Yomichan 字典 | 自实现解析 | 格式开放（JSON/SQLite），写解析器 | 中 |
| 汉字数据 | kanjivg/kanjidic/jpdb | 均为 JSON/SQLite 文件，直接加载 | 易 |
| 活用还原 | deconjugator_v2 | `deconjugator.json` 是纯数据+规则，重写规则引擎 | 中（数据可移植） |
| 声调 | accents.txt | 纯数据加载显示 | 易 |
| LLM（Gemini） | google.genai | Google.Cloud.AIPlatform 或裸 HTTP 调 generativelanguage API | 易 |
| FSRS 间隔重复 | fsrs（含 optimizer） | 有 C# 社区移植（如 FsrsSharp），算法开源 | 中 |
| Anki 同步 | anki_connect | 纯 HTTP，AnkiConnect 协议开放 | 易 |
| Anki 导出（.apkg） | genanki | ⚠️ **无成熟 C# 库**；.apkg=SQLite+zip 可手写，或仅做 AnkiConnect 实时同步 | 中（唯一真缺口） |
| GUI | PySide6/Qt6 | WPF / WinUI3 / Avalonia | 易（C# GUI 反而更强） |
| HTTP API | FastAPI | ASP.NET Core minimal API | 易（更强） |
| 云后端 / 推荐器 | FastAPI + GCS | ASP.NET Core + 任意对象存储 | 中（本地优先版可不做云） |
| 打包发布 | PyInstaller(~96MB) | .NET 单文件自包含发布 | 易（C# 明显更优） |

### 真正的"库缺口"只有 3 处（均非死路）

1. **`genanki` 生成 `.apkg`**：C# 没有对应库。绕过方式：用 AnkiConnect 实时同步（DokiDokiDict 本身也用了），或手写 `.apkg`（SQLite + zip，格式公开）。
2. **`stable_whisper` 词级时间戳稳定**：无 C# 移植。绕过方式：whisper.net 自带 word-level timestamps，或自行实现稳定逻辑。
3. **HuggingFace Hub 自动下载模型**：C# 无一等公民客户端。绕过方式：直链 HTTP 下载 `.onnx`/模型文件，首次运行拉取并缓存。

### 关于 Python 生态"广度"的错觉

安装包内那一大套 torch / transformers / scikit-learn / pandas / matplotlib / optuna / lightning，**大部分是 faster_whisper、stable_whisper 与推荐器统计图表的传递依赖**，并非每个都是独立必需功能。C# 做推理只需 ONNX Runtime，做图表用 ScottPlot/OxyPlot，不需要 pandas/matplotlib。不要被依赖列表的体量吓到——真正需要"替换"的是上表中列出的具体能力点。

## 工程量与开发时间线参考

GitHub API 与 git 记录显示的真实节奏（用于校准预期，非精确工期）：

- 源码仓库创建：2026-02-25；首个公开 release 已是 v0.9.3（2026-03-09），说明 0.1～0.9.2 均在 2026-02 之前的私有开发中完成。
- 公开发布：v0.9.3（03-09）→ v0.9.4（04-17）→ v0.9.5（07-27）。
- git 仅有 2 个 commit（v0.9.4、v0.9.5 两个发布快照），v0.9.4 → v0.9.5 之间约 3.3 个月塞进 197 文件、+191k 行，真实提交历史被压成快照未公开。
- 保守估计：**至少半年以上持续开发，且仍在 0.9.x、未到 1.0**，且这是在直接复用 PySide6 / faster_whisper / onnxruntime / fsrs / genanki / google-genai 等现成库的前提下。

推论：连站在 Python 库巨人肩上的原作者都做了大半年仍未到 1.0，全量克隆不现实。这与 [竞品快照](./dokidokidict-competitive-snapshot.md) 中"VN-Learning 不应在 MVP 验证阶段做功能追赶"的判断一致。

## 建议路线（延续 VN-Learning 的 .NET 10 项目）

1. **不要全量克隆**。按核心价值裁剪：截图 OCR → MeCab 分词 → JMdict 查词 → LLM 中文释义（VN-Learning 已基本完成），先跑通"看图查词"闭环。
2. **OCR 分阶段**：先接 Google Lens 或 owocr（零模型成本起步），再补本地 meikiocr ONNX（用 Microsoft.ML.OnnxRuntime 跑那 3 个 `.onnx`，照 `src/ocr/providers/meikiocr/provider.py` 翻译前后处理）。
3. **Whisper 用 Whisper.net**，音频挖矿作为二期。
4. **SRS / Anki 先用 AnkiConnect**，跳过 `.apkg` 生成。
5. **推荐器 / 云后端 / 成就系统**后置或不做，除非构成产品的差异化卖点。
6. **保留并强化 VN-Learning 的既定差异点**：中文语境语法/词块解释、本地候选+可校验 LLM 输出、BYOK 隐私与本地回退——这些是 DokiDokiDict 未覆盖的窄而深定位，参见 [VN-Learning 项目计划](../../VN-Learning-Project.md)。

## 待验证事项

本文的可行性判断基于库的存在性与模型可移植性，尚未实测以下环节，建议在动手前各做一次最小验证：

- **本地 meikiocr ONNX 在 C# ONNX Runtime 下的推理结果与 Python 版一致性**（检测/识别阈值、竖排文本）。
- **Whisper.net 在目标硬件上的延迟与词级时间戳质量**，对标 faster_whisper + stable_whisper。
- **Yomichan 字典格式在 C# 的解析完整度**（含自定义字典的边缘字段）。
- **FsrsSharp 与 Python `fsrs` 调度参数兼容性**（若选择内置 SRS）。

## 来源登记

1. 安装版二进制：`C:\Program Files (x86)\DokiDokiDict`（v0.9.5，文件日期 2026-07-23），含 `_internal\` 全部依赖与数据文件。本文功能清单、技术栈、ONNX 模型与词典数据均由此及源码实证。
2. 源码仓库：`D:\project\DokiDokiDict`，`git remote = https://github.com/elwendys/DokiDokiDict.git`，最新 commit `ed17103 Release v0.9.5 source`。本文模块图与 OCR/词典/SRS 实现细节均来自此源码。
3. GitHub API（2026-07-31 查询）：`repos/elwendys/DokiDokiDict`（created_at 2026-02-25）、`repos/elwendys/DokiDokiDict-releases/releases`（v0.9.3/0.9.4/0.9.5 发布时间）。
4. 本地项目记忆与 VN-Learning 已有验证：MeCab（LibNMeCab）、JMdict、DeepSeek LLM 在 .NET 10 的可用性，见仓库 [VN-Learning 项目计划](../../VN-Learning-Project.md)。
