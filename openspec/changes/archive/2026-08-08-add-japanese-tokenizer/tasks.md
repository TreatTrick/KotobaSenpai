# Tasks: 日语分词器

## 1. Core：端口、模型、错误码

- [x] 1.1 新增 `src/KotobaSenpai.Core/Contracts/ITokenizer.cs`：定义 `interface ITokenizer { IReadOnlyList<Token> Tokenize(string? text); }`；null、空字符串和空白输入返回空列表，不新增未使用的 `TokenizationResult` 包装类型
- [x] 1.2 新增 `src/KotobaSenpai.Core/Models/Token.cs`：不可变 record，字段包含 Surface、Lemma、OrthBase、Reading（Kana）、BaseReading（KanaBase）、Pronunciation（Pron）、四级 PartsOfSpeech、ConjugationType、ConjugationForm、原始 AType、StartOffset；防御性复制集合字段，校验 Surface 非空且 StartOffset>=0
- [x] 1.3 `src/KotobaSenpai.Core/Localization/ErrorCodes.cs` 新增 `UniDicDictionaryMissing`、`UniDicDictionaryInvalid`、`UniDicDownloadFailed`

## 2. 固定依赖、数据资产与许可证

- [x] 2.1 `KotobaSenpai.Platform.Windows.csproj` 添加并冻结 `LibNMeCab` 0.10.2；记录 nupkg SHA-256，禁止浮动版本
- [x] 2.2 新增可审计的 tokenizer asset manifest：记录 Doki 兼容 `unidic-py` 构建 `3.1.0+2021-08-31`、固定下载 URL、词典压缩包 SHA-256、预期 `unidic22` 格式和四个运行时文件；在实现前计算并写入实际哈希
- [x] 2.3 更新 `THIRD-PARTY-NOTICES`：分别记录 LibNMeCab `GPL-2.0-or-later OR LGPL-2.1-or-later`、UniDic 数据 BSD 许可、`unidic-py` 构建来源、版本、URL 和哈希
- [x] 2.4 记录 single-file、裁剪、AOT 或程序集嵌入场景下的 LGPL 可替换/重新链接验证结果；未验证前不得写“可安全商用”结论

## 3. Platform.Windows：分词器适配器

- [x] 3.1 新增 `src/KotobaSenpai.Platform.Windows/Japanese/UniDicTokenizer.cs : ITokenizer, IDisposable`：`Lazy<MeCabUniDic22Tagger>` 懒加载；目录解析为 `KOTOBA_UNIDIC_DIR` 覆盖 → `%LocalAppData%/KotobaSenpai/UniDic/dicdir`；支持测试注入路径
- [x] 3.2 加载前校验 `char.bin`、`matrix.bin`、`sys.dic`、`unk.dic`；默认缓存目录还需通过已安装 manifest，环境变量覆盖目录可没有项目 manifest，但必须通过版本/格式校验；缺失抛 `UniDicDictionaryMissing`，适用的版本/格式/哈希不匹配抛 `UniDicDictionaryInvalid`；`uni.dic`、`model.bin` 不作为运行时必需项
- [x] 3.3 `Tokenize` 使用 `MeCabUniDic22Node` 的 `Surface`、`Pos1..Pos4`、`CType`、`CForm`、`Lemma`、`OrthBase`、`Kana`、`KanaBase`、`Pron`、`AType` 等强类型属性；禁止 `Feature.Split(',')`
- [x] 3.4 计算 `StartOffset = node.BPos + (node.RLength - node.Length)`（UTF-16 code-unit 索引），跳过 BOS/EOS，保留输入中的空格和换行；空/空白输入直接返回空列表
- [x] 3.5 对共享 tagger 的 Parse 和节点投影提供并发保护，释放已创建的 tagger，且懒加载失败不缓存半初始化实例

## 4. App：词典安装与原子替换

- [x] 4.1 新增 `src/KotobaSenpai.App/Japanese/UniDicDictionaryInstaller.cs`：默认目标 `%LocalAppData%/KotobaSenpai/UniDic/dicdir`；依赖路径、`HttpClient`、manifest 可注入
- [x] 4.2 实现 `IsInstalled`：四个运行时二进制、版本/格式和 manifest 均有效时才返回 true；残留 staging 或缺文件视为未安装/无效
- [x] 4.3 实现在线 `EnsureInstalledAsync(IProgress<double>?, CancellationToken)`：固定 URL 下载到临时 zip → SHA-256 校验 → staging 解压 → 内容探测 → 校验 `version`/`dicrc` → 写 manifest → 同卷原子移动；不解析 `latest`
- [x] 4.4 实现离线 `InstallFromArchiveAsync`：复用在线流程的哈希、版本、格式、文件集和原子替换验证，不依赖网络
- [x] 4.5 增加取消、进程内/跨进程安装锁、失败清理和重试行为；安装失败不得破坏已有有效词典，`model.bin` 可保留但不可作为成功条件

## 5. DI、启动和本地化

- [x] 5.1 `App.xaml.cs` `ConfigureServices` 注册 `ITokenizer → UniDicTokenizer` 和 `UniDicDictionaryInstaller` 为单例，并共享默认目录策略
- [x] 5.2 `OnStartup` 通过可观察的后台包装任务触发安装（不阻塞主窗）；捕获并记录三个词典错误码，禁止产生未观察的 fire-and-forget 异常
- [x] 5.3 `Strings.resx` + `Strings.zh-CN.resx` 新增 `UniDicDictionaryMissing`、`UniDicDictionaryInvalid`、`UniDicDownloadFailed`（EN/ZH），文案可指导重试或离线导入

## 6. 测试：黄金语料、完整性与并发

- [x] 6.1 `tests/KotobaSenpai.Platform.Windows.Tests/UniDicTokenizerTests.cs` 使用固定 manifest 词典（CI 预置或显式 `KOTOBA_UNIDIC_DIR`，不依赖开发机绝对路径），覆盖日本/買った/覆う/アルミホイル、口语、拟声词、专名、拉长音和未知标点
- [x] 6.2 断言 Surface、Lemma、OrthBase、Reading/Kana、BaseReading/KanaBase、Pronunciation/Pron、POS1..4、活用字段、原始 AType（含引号/多值字段）以及固定黄金输出；验证 `(OrthBase, BaseReading)` 可形成稳定的后续查词键，但不在本 change 执行 JMdict 查询；不宣称 Doki 最终音高一致
- [x] 6.3 增加空/空白/null、空格/换行、重复 token、非 BMP 字符输入的 UTF-16 StartOffset 测试，并断言源文本切片与 Surface 一致
- [x] 6.4 增加同一 tokenizer 单例的多线程压力测试，验证结果确定、无异常、无字段串扰
- [x] 6.5 `tests/KotobaSenpai.App.Tests/UniDicDictionaryInstallerTests.cs` 使用小型 zip fixture 覆盖：顶层目录探测、四文件检查、manifest/版本/格式校验、成功原子安装、已安装短路、哈希失败、网络/解压失败、取消、失败清理和离线导入；不联网
- [x] 6.6 增加并发安装/跨进程锁测试，确认不会暴露半成品 `dicdir` 且已有有效安装不会被破坏
- [x] 6.7 架构测试确认 `ITokenizer` 在 Core、`UniDicTokenizer` 在 Platform.Windows、installer 只位于 App；必要时补充 `DependencyDirectionTests`
- [x] 6.8 记录冷启动词典加载、warm tokenization 延迟、峰值内存和磁盘占用基准，决定是否从 NMeCab-only 缓存移除 `model.bin`

## 7. 验证与收尾

- [x] 7.1 更新 `docs/spike-nmecab/src/SpikeLib/Program.cs`：使用强类型 UniDic 属性断言，不再使用 `Feature.Split(',')` 展示字段
- [x] 7.2 手动：设 `KOTOBA_UNIDIC_DIR` 指向固定词典目录，验证 `ITokenizer.Tokenize("日本語")`、空白偏移和安装错误码
- [x] 7.3 `dotnet build` 全绿（Core/Platform/App 均 TreatWarningsAsErrors）
- [x] 7.4 `dotnet test` 全绿（含 tokenizer、installer、架构、并发和完整性测试）
- [x] 7.5 `openspec validate "add-japanese-tokenizer" --type change --strict` 通过；实现完成且所有任务勾选后再执行 `openspec archive --yes`
