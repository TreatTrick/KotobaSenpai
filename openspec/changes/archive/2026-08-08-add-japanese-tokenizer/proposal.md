# Proposal: 日语分词器（冻结的 LibNMeCab + UniDic 3.1.0 基线）

## Why

KotobaSenpai 已用 meikiocr 完成日语 OCR（截图→字符），下一步是分词层：把 OCR 出的日文切成词并产出词元、读音、词性、活用和字符偏移，供后续词典查词与“语境语法/词块解释”使用。当前仓库尚无任何分词器。

参考实现 DokiDokiDict 使用 Python 的 `fugashi` + `unidic`，而 KotobaSenpai 必须保持纯 C#。审计和 .NET 10 spike 证明：`LibNMeCab 0.10.2` 的 `MeCabUniDic22Tagger` 可以加载固定的 UniDic 3.1.0 格式数据，并通过强类型节点属性读取 29 列字段。这个结论是项目级兼容验证，不是 LibNMeCab 对 UniDic 3.1.0 的上游认证，也不等同于 Doki 的字节级或最终音高结果一致。

本变更以复现 Doki Windows release 所使用的 `unidic-py` 数据构建（`3.1.0+2021-08-31`）为优先目标。该词典资产、NuGet 包版本、来源 URL、SHA-256 和许可证信息必须冻结并随发布物记录。

## What Changes

- **新增分词端口与模型**：Core 层 `ITokenizer` 端口和不可变 `Token` 模型，保留表面形、UniDic lemma、`orthBase`、`kana`/`kanaBase` 读音、`pron` 发音形、POS1..4、活用信息、原始 `aType` 和 .NET UTF-16 字符偏移。
- **新增分词器适配器**：Platform.Windows 层 `UniDicTokenizer` 包装 `MeCabUniDic22Tagger`，只使用 `MeCabUniDic22Node` 的强类型属性，不在应用层拆分 CSV；支持 `KOTOBA_UNIDIC_DIR` 覆盖和并发安全调用。
- **新增可验证的词典安装**：App 层 `UniDicDictionaryInstaller` 使用固定 URL + SHA-256 下载并校验 Doki 兼容的 UniDic 3.1.0 数据，安装运行时必需的 `char.bin`、`matrix.bin`、`sys.dic`、`unk.dic`，并提供离线压缩包导入路径。
- **新增错误码与本地化**：`UniDicDictionaryMissing`、`UniDicDictionaryInvalid`、`UniDicDownloadFailed`，配套中英文文案。
- **依赖与资产冻结**：Platform.Windows 引用 `LibNMeCab` 0.10.2；记录 NuGet 包哈希、词典哈希、版本清单和来源。
- **发布合规**：分别记录 LibNMeCab 的 `GPL-2.0-or-later OR LGPL-2.1-or-later` 与 UniDic 数据的 BSD 许可，并增加 single-file/LGPL 可替换性检查。

## Capabilities

### New Capabilities
- `japanese-tokenizer`: 日语分词——对日文文本产出 UniDic 词元序列和可复现的字符偏移，词典按固定版本校验后下载或从离线包安装并本地缓存。

### Modified Capabilities

无（本次引入独立能力，不改动既有 spec 的需求）。

## Impact

- **Core**：新增 `ITokenizer` 端口、`Token` 模型、`ErrorCodes` 三个词典相关错误码（镜像 OCR 的错误处理模式）。
- **Platform.Windows**：新增 `UniDicTokenizer` 适配器；csproj 添加 `LibNMeCab` 0.10.2。
- **App**：新增 `UniDicDictionaryInstaller`（固定资产校验、在线下载和离线导入）；`App.xaml.cs` 注册 DI，并以可观测的后台任务触发安装，失败不得产生未观察异常。
- **本地化**：`Strings.resx` + `Strings.zh-CN.resx` 新增三个错误文案键。
- **文档**：`THIRD-PARTY-NOTICES` 分别记录 UniDic 数据、`unidic-py` 数据构建和 LibNMeCab 的许可证与来源；增加依赖/词典 manifest。
- **Non-Goals（本次不做）**：不接入既有“识别→取词”UI 流；不实现 Doki 的人工音高覆盖、Kanjium 查询或最终音高合成；不宣称字节级输出一致；不追逐未验证的最新 UniDic 版本；不做下载断点续传（失败整体重试即可）。
