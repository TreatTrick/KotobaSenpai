# DokiDokiDict 竞品快照

**调研日期：** 2026-07-29  
**调研范围：** DokiDokiDict 官方 itch.io 页面及其直接链接的第一方内容。未使用评论、论坛帖子、代码仓库或其他二手资料。

## 结论

DokiDokiDict 与 VN-Learning 在“从屏幕上的日文到查词结果”的链路上高度重叠：两者都面向 Windows 桌面环境、视觉小说或游戏中的日文、基于屏幕的阅读辅助、振假名、词典释义，以及按上下文选择词义。DokiDokiDict 页面描述的是范围更大的、可安装的面向最终用户产品：还包含内置 SRS、Anki 导入导出、单词状态标记、阅读统计与词汇驱动的内容推荐。[官方产品页](https://magicshot.itch.io/dokidokidict)

这并不等于两个项目在功能上完全相同。VN-Learning 的既定 MVP 差异点是：以中文解释语境词义，尤其解释连续与非连续的语法/词块；以本地候选词块、经校验的 LLM 输出、可编辑 OCR 文本，以及明确的本地回退和隐私行为支撑该体验。这些仍只是产品计划中的主张，并非已被验证的优势；在将其视为可防御的差异前，必须进行真实的横向评测。参见 [VN-Learning 项目计划](../../VN-Learning-Project.md)。

## DokiDokiDict：已声明的产品与运行模式

| 维度 | 第一方表述 | 置信度与边界 |
| --- | --- | --- |
| 交付方式 | Windows 可下载工具，提供 `DokiDokiDict_Setup.exe`（826 MB），可自行填写价格下载。 | 在[下载和平台区域](https://magicshot.itch.io/dokidokidict#download)直接声明。 |
| 产品状态 | itch.io 页面标注为“开发中”；调研时页面显示最近更新于 2026-07-27 UTC。 | 页面元数据，属于时效性快照。[更多信息](https://magicshot.itch.io/dokidokidict) |
| 主要交互 | “按住 Shift，指向文本，即可获得释义”；其被描述为“可在屏幕任意位置使用”的日语弹窗词典，且无需文本 Hook。 | 直接声明。这支持基于屏幕的取词，但页面未说明 OCR 引擎、捕获 API、延迟或兼容性矩阵。[产品说明](https://magicshot.itch.io/dokidokidict) |
| 适用内容 | 游戏、视觉小说、漫画与网站，只要屏幕上有日文即可读取。 | 营销主张；页面未公开兼容性或准确率矩阵。[产品说明](https://magicshot.itch.io/dokidokidict) |
| 本地与远程 | MeCab 振假名被描述为即时、完全离线；页面另称使用 Gemini 根据上下文排序释义。 | 页面未说明发送给 Gemini 的数据、凭据/计费方式、离线回退、数据保留或调用时机。[振假名和 AI 释义排序部分](https://magicshot.itch.io/dokidokidict) |
| 词典 | 内置 JMdict；可导入 Yomichan 词典；支持不同热键对应多个词典配置，也支持在固定弹窗内二次查词。 | 直接声明。[多词典](https://magicshot.itch.io/dokidokidict) |

## DokiDokiDict：已声明的具体功能

| 能力分组 | 第一方描述 |
| --- | --- |
| 弹窗查词与阅读辅助 | Shift 触发的弹窗释义、MeCab 振假名、动态更新振假名，以及可选的音高重音着色和假名词上方标记。[产品说明](https://magicshot.itch.io/dokidokidict) |
| 语境释义 | Gemini 会读取用户正在阅读的内容，并将多义词最合适的义项排到最前面。[AI 释义排序](https://magicshot.itch.io/dokidokidict) |
| 单词状态辅助 | 对屏幕文字以下划线区分成熟卡片、牌组内单词、重复出现的单词和未知词；可提醒 i+1 句；可隐藏用户应已掌握单词的释义，形成回忆挑战。[单词追踪、回忆与 i+1](https://magicshot.itch.io/dokidokidict) |
| 内置复习 | 内置 SRS 卡片可包含单词、读音、释义、例句与截图；可在专用标签页复习，或在阅读自然停顿时出现；高频遇到的单词可自动加入，并可先进行突击测验。[内置 SRS](https://magicshot.itch.io/dokidokidict) |
| Anki 工作流 | `S` 创建卡片；`E` 可导出带读音、释义、带振假名例句、截图与音高重音的条目；还声称内置 SRS 与 Anki 可双向导入导出，到期日会同步转移。[内置 SRS 与 Anki 集成](https://magicshot.itch.io/dokidokidict) |
| 进度与内容选择 | 自动记录阅读与查词数据；高级统计提供 25 个预设；支持每日阅读目标与 139 项成就；推荐器可按已知词汇给 VN、书籍、动画和游戏排序，并能根据词汇目标或页数预算生成阅读顺序。[统计、连续记录与成就；个性化阅读推荐](https://magicshot.itch.io/dokidokidict) |

## 与仓库既定 MVP 的对比

下表对比 DokiDokiDict 的官方主张与 `VN-Learning-Project.md` 中记录的 VN-Learning 范围；并不声称已经实际运行并观察过任一产品的行为。

| 决策维度 | DokiDokiDict 官方主张 | VN-Learning 既定 MVP | 实际含义 |
| --- | --- | --- | --- |
| 捕获与查词 | 面向整个屏幕的 Shift 查词，无需文本 Hook，适用于游戏、VN、漫画和网站。 | 选择目标窗口，通过全局快捷键按需捕获/OCR 一块区域；手动输入与手动框选是回退方式。 | 用户问题与核心交互高度重叠。 |
| 基础语言辅助 | 释义、振假名、音高重音、JMdict/Yomichan 词典。 | 本地分词/振假名，加上中文的语境释义和例句。 | 查词和振假名高度重叠；释义语言与呈现质量需要在真实语料上比较。 |
| 上下文处理 | Gemini 根据阅读内容为多义词排序词典义项。 | 本地规则/词典生成词块候选，包括非连续结构；LLM 在版本化契约下给出中文词义、语法作用、例句和置信度。 | 这是最清晰的预期差异：解释语法/词块结构，而不只是排序词典义项。但只有在同一批句子上与 DokiDokiDict 比较后才能成立。 |
| 学习系统 | 单词状态覆盖层、i+1、回忆模式、内置 SRS、Anki、统计、成就和内容推荐。 | MVP1 明确不包括 Anki、长期单词/句子存储、SRS、云同步、订阅和移动端。 | DokiDokiDict 的学习管理范围明显更广；VN-Learning 不应在 MVP 验证阶段做功能追赶。 |
| 隐私与失败处理 | 声明 MeCab 振假名可离线运行，Gemini 用于义项排序；其余数据流与回退细节未公开。 | 计划中包括本地 OCR/分词/缓存，默认只发送文本而非截图的 BYOK DeepSeek 请求，清缓存能力、Schema 校验和本地结果回退。 | 这可能构成有意义的信任和可靠性定位，但须以用户可见的实现和文档证明，不能只停留在计划。 |

## 证据边界与建议决策

官方页面的功能描述很丰富，但没有公开 API、源代码、已支持游戏矩阵、OCR 指标、端到端延迟、除“自行填写价格”外的定价，或数据处理细节。不要从页面的 `ocr` 或 `No AI` 标签推断更多：正文明确提到 Gemini 释义排序，而页面的 “No generative AI was used” 内容标签具体所指未被解释。

继续广泛开发之前，应使用有代表性的 VN 台词做一轮小型横向评测：OCR 恢复能力、词义选择正确率、语法/词块解释正确率（尤其是非连续结构）、出结果时间，以及中文学习者是否认为解释可直接帮助阅读。可行的定位是更窄、更可审计的“面向中文 VN 学习者的语境语法与词块解释器”，而不是泛化的屏幕 OCR 词典或完整 SRS 平台。

## 来源登记

1. [DokiDokiDict by magicshot，官方 itch.io 产品页](https://magicshot.itch.io/dokidokidict)，访问于 2026-07-29。本文所有外部产品描述，包括产品说明、下载信息、平台/状态元数据和页面链接的第一方截图，均来自该页面。
2. [VN-Learning Windows MVP1 项目计划](../../VN-Learning-Project.md)，本地仓库文档，访问于 2026-07-29。本文所有 VN-Learning 范围与对比描述均以此为依据。
