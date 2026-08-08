# Design: 日语分词器（冻结的 LibNMeCab + UniDic 3.1.0 基线）

## Context

KotobaSenpai 是纯 C#/.NET 10 WPF 应用，采用端口适配器（六边形）三层架构：Core（纯领域，零外部依赖）/ Platform.Windows（适配器）/ App（组合根）。已有 meikiocr 字符级 OCR；本变更增加独立的日语形态素分析能力，不把分词接入既有 UI 流。

DokiDokiDict 使用 Python `fugashi` + `unidic`。本项目不能依赖 Python 运行时，因此选择 `LibNMeCab 0.10.2`。项目 spike 证明 `MeCabUniDic22Tagger` 能加载 UniDic 3.1.0 的 `unidic22` 格式，并读取 29 列字段；这是项目级格式/二进制兼容证据，不是 LibNMeCab 上游对 UniDic 3.1.0 的单独支持承诺。

对齐目标是 Doki Windows release 使用的 `unidic-py` 构建 `3.1.0+2021-08-31`，而不是笼统的“UniDic 3.1.0”。该构建修改过部分未知词和词条数据，所以只有固定同一数据包并校验 SHA-256，才可谈数据级复现；即使词典相同，也不承诺 Doki 项目级音高覆盖和外围规则完全一致。

LibNMeCab 的 NuGet 许可证表达式为 `GPL-2.0-or-later OR LGPL-2.1-or-later`，与 UniDic 数据的 BSD 许可分开处理。词典目录约 811.6 MB（十进制）/774 MiB；NMeCab 0.10.2 实际运行时读取 `char.bin`、`matrix.bin`、`sys.dic`、`unk.dic`，不要求 `uni.dic` 或 `model.bin`。

## Goals / Non-Goals

**Goals:**

- 提供同步的 `ITokenizer.Tokenize(string? text) -> IReadOnlyList<Token>`，返回可用于后续查词的 UniDic 字段和 .NET UTF-16 字符偏移。
- 固定并可审计 LibNMeCab 包与词典数据版本、来源、SHA-256 和许可证。
- 使用强类型 `MeCabUniDic22Node` 属性解析字段，保留完整四级词性和原始 `aType`，不在应用层自行拆分 CSV。
- 首次需要时支持固定 URL 校验下载，也支持导入已校验的离线压缩包；安装过程可取消、可重试且不会留下半成品。
- 保持适配器位于 Platform.Windows、安装器位于 App，并让单例分词器的并发行为确定且可测试。

**Non-Goals:**

- 不接入既有“识别→取词”UI 流；接入属于后续变更。
- 不实现振假名显示、JMdict 查询、语法词块生成或 Doki 的人工音高覆盖/Kanjium 合成。
- 不宣称与 Doki 或某个词典包字节级输出一致；黄金样例只约束本项目固定资产的可复现结果。
- 不追逐最新 UniDic 版本；升级 3.1.1 或其他 CWJ 版本必须另做基准和数据迁移评估。
- 不做断点续传或多分片下载；失败后整体重试即可。

## Decisions

### D1. 冻结分词库和词典资产

- 采用 `LibNMeCab 0.10.2` + `MeCabUniDic22Tagger`。仓库和包仍可用，但上游发布和代码维护长期低频，因此把它当作经过项目兼容测试的冻结依赖，并记录 NuGet nupkg SHA-256。
- 采用 Doki 兼容的 `unidic-py` 数据构建 `3.1.0+2021-08-31`。固定下载 URL 为 `https://cotonoha-dic.s3-ap-northeast-1.amazonaws.com/unidic-3.1.0.zip`；合并实现前必须在 manifest 中写入实际 SHA-256，禁止使用 `latest` 或未校验的镜像。
- `dicrc` 的 `output-format-type = unidic22`、字段顺序和二进制版本必须在安装验收中检查。兼容性依据为固定版本源码、词典文件和本机 spike，而非上游认证矩阵。
- 备选方案：Python.NET/fugashi 会引入 Python 运行时；MeCab.DotNet 1.2.0 虽发布较新，但没有 UniDic 强类型节点且仍有 GPL/LGPL 许可证；两者暂不替换当前已验证路径。

### D2. Core 端口和 Token 模型

- `ITokenizer` 放在 Core，公开同步方法 `IReadOnlyList<Token> Tokenize(string? text)`。MeCab 分析是同步 CPU 工作，不为本变更引入异步端口。
- `null`、空字符串和仅包含空白的输入返回空列表；非空输入不修改原文。
- `Token` 使用不可变 record，至少包含：`Surface`、UniDic `Lemma`、`OrthBase`、`Reading`（`Kana`/仮名形出現形）、`BaseReading`（`KanaBase`/仮名形基本形）、`Pronunciation`（`Pron`/発音形出現形）、四级 `PartsOfSpeech`、`ConjugationType`、`ConjugationForm`、原始 `AType`、`StartOffset`。
- `Reading`/`BaseReading` 保留 Doki 查词路径所依赖的 `kana`/`kanaBase` 字段；`Pronunciation` 单独保留发音形，不能把三者合并为一个含义不明的“读音”字段。
- `PartsOfSpeech` 必须保留 pos1..pos4 四个值；字段缺失时保留空字符串而不是改变列表长度。`AType` 是 UniDic 原始字符串，可能为空或包含多值/引号语义，不能解释为 Doki 的最终音高。
- `StartOffset` 定义为输入 .NET 字符串中的 UTF-16 code-unit 起点，非字节偏移、非 Unicode code-point 偏移。构造时校验 `Surface` 非空、偏移非负。
- 不定义未被使用的 `TokenizationResult` 包装类型；调用方只接收 token 列表。

### D3. Platform.Windows 分词器适配器

- `UniDicTokenizer : ITokenizer, IDisposable` 位于 Platform.Windows，使用 `Lazy<MeCabUniDic22Tagger>` 延迟加载单个词典实例。
- 目录解析顺序为 `KOTOBA_UNIDIC_DIR`（非空时取规范化绝对路径）→ `%LocalAppData%/KotobaSenpai/UniDic/dicdir`。安装器和分词器使用同一默认路径；测试通过构造函数注入临时根目录，避免并行测试修改进程级环境变量。
- 加载前检查 `char.bin`、`matrix.bin`、`sys.dic`、`unk.dic`。默认缓存目录必须同时满足项目 manifest、版本和格式校验；`KOTOBA_UNIDIC_DIR` 外部覆盖目录可没有项目 manifest，但仍必须满足运行时文件、版本和 `unidic22` 格式校验。缺少运行时文件抛 `UniDicDictionaryMissing`；存在文件但适用的 manifest、版本或格式校验失败抛 `UniDicDictionaryInvalid`。`uni.dic` 和 `model.bin` 对该运行路径不是必需文件。
- `Tokenize` 只读取 `MeCabUniDic22Node` 的 `Surface`、`Pos1..Pos4`、`CType`、`CForm`、`Lemma`、`OrthBase`、`Kana`、`KanaBase`、`Pron`、`AType` 等属性；禁止直接调用 `Feature.Split(',')`。NMeCab 的 `GetFeatureAt` 已包含 CSV 状态机，可正确处理带引号和逗号的字段。
- 对普通节点使用 `StartOffset = node.BPos + (node.RLength - node.Length)`，从而保留词前空格/换行；跳过 BOS/EOS 节点。该公式与 NMeCab 的 UTF-16 字符位置定义一致，不用前序表面长度累加。
- `Parse` 调用和节点投影在同一同步保护范围内，保证单例适配器的并发调用结果不互相污染；实现完成后以多线程黄金测试验证，若上游行为证明线程安全仍保留等价的安全约束。
- 释放适配器时释放已创建的 tagger；懒加载失败不得缓存一个可继续使用的半初始化实例。

### D4. 词典安装、完整性和双轨分发

- `UniDicDictionaryInstaller` 位于 App，默认目标为 `%LocalAppData%/KotobaSenpai/UniDic/dicdir`。构造函数接受可注入的目标根目录、`HttpClient`、固定 manifest 和临时目录策略，便于无网络单元测试。
- `IsInstalled` 只有在四个运行时二进制、期望版本/格式和已校验 manifest 均满足时才返回 true；仅有三个文件或残留临时目录不算已安装。
- 在线流程：已安装则立即返回；否则下载固定 URL 到临时 zip，支持取消；下载完成后计算 SHA-256，哈希不匹配抛 `UniDicDictionaryInvalid`；解压到同卷 staging 目录，按内容探测包含四个运行时文件的目录，并验证 `version`/`dicrc`；验证通过后原子移动到最终 `dicdir`，写入 manifest，最后清理 staging。
- 离线流程复用同一验证和安装逻辑，从用户选择的本地压缩包导入；离线包也必须包含可核对的版本和 SHA-256，不能绕过完整性检查。
- `model.bin` 可随完整离线包保留，但不能成为运行时安装成功的必要条件；是否在 NMeCab-only 缓存中删除它由后续体积基准决定。`uni.dic` 同样不列入必需文件。
- 安装使用进程内和跨进程锁，避免多个应用实例同时替换 `dicdir`；任何取消、网络、解压、校验或移动失败都清理临时/半成品并保留可重试状态。

### D5. DI、启动和错误可观测性

- `App.xaml.cs` 注册 `ITokenizer -> UniDicTokenizer` 和 `UniDicDictionaryInstaller` 为单例，并共享同一默认词典路径策略。
- 启动后台安装必须通过可观察的包装任务执行：捕获并记录 `UniDicDictionaryMissing`/`UniDicDictionaryInvalid`/`UniDicDownloadFailed`，不得产生未观察的 fire-and-forget 异常；分词调用仍返回可由 UI 处理的错误码。
- 后续 UI 可根据安装状态触发重试或离线导入；本变更不实现 UI 流程。

### D6. 许可证和 single-file 发布门禁

- `THIRD-PARTY-NOTICES` 分别列出 LibNMeCab 的 GPL/LGPL 双许可、UniDic 数据的 BSD 许可、`unidic-py` 构建来源和对应版本/哈希。
- 发布配置必须选择并记录 LGPL 合规路径，验证 single-file、裁剪、AOT 或程序集嵌入后用户仍能替换/重新链接 LibNMeCab，或改用可替换的外置程序集分发；在验证完成前不得写“可安全商用”结论。

## Risks / Trade-offs

- **[上游低频维护]** → 固定 NuGet 版本和包哈希；保留 MeCab.DotNet 作为备选，定期用同一黄金语料和基准复核。
- **[Doki 与固定词典/外围逻辑差异]** → 明确只保证字段语义和项目黄金样例；不承诺字节级输出或最终音高一致。
- **[下载源漂移、损坏或不可达]** → 固定 URL + SHA-256 + manifest；提供离线导入包；失败清理 staging 并可整体重试。
- **[词典占用约 774 MiB]** → 默认缓存到 LocalAppData；在线缓存可在验收后移除 `model.bin`，完整离线包仍可保留它。
- **[LGPL 与 single-file/AOT 合规复杂]** → 将许可证检查列为发布门禁，分别提供许可证文本、来源和替换/重新链接验证证据。
- **[单例 tagger 并发行为未由上游保证]** → 适配器串行化 `Parse` 或使用等价线程隔离，并以压力测试锁定行为。
- **[安装进程崩溃留下半成品]** → staging + 原子移动 + 跨进程锁；`IsInstalled` 拒绝未完成 manifest。

## Migration Plan

1. 先冻结依赖 manifest（NuGet 包、词典 URL/版本/SHA-256/许可证）并更新第三方 notices。
2. 实现 Core 端口/模型/错误码 → Platform 分词器 → App 安装器/DI → 本地化 → 测试和基准。
3. 发布时同时提供固定 URL 的在线安装资产和可导入的离线词典包。
4. 回滚：移除 DI 注册和安装启动任务即可；新增 Core 类型不破坏既有 OCR/overlay 行为。

## Open Questions

- 实际词典 SHA-256 和 LibNMeCab nupkg SHA-256 必须在实现前计算并写入 manifest；未写入前不得标记任务完成。
- 是否从在线安装缓存中删除 `model.bin` 由验收语料、冷启动时间和磁盘占用基准决定；无论选择哪种，完整性清单必须明确实际分发文件。
- Doki 的人工音高覆盖、Kanjium 和 JMdict 接入属于后续变更，不在本 change 中解决。
