# japanese-tokenizer Specification

## Purpose
TBD - created by archiving change add-japanese-tokenizer. Update Purpose after archive.
## Requirements
### Requirement: Tokenize Japanese text
系统 SHALL 将日语文本分词为 token 序列。每个 token SHALL 暴露 surface、UniDic lemma、书写基本形 `orthBase`、表面形读音 `kana`、基本形读音 `kanaBase`、发音 `pron`、四级词性、活用类型、活用形、原始 UniDic `aType`、可选的规范化音调核以及原始输入中的起始偏移。规范化音调核 SHALL 使用 `0` 表示平板型、`1` 表示头高型、`N` 表示第 N 拍后下降；原始值缺失或无效时 SHALL 为空。

#### Scenario: 分词简单句
- **WHEN** 调用方使用日语文本 `日本語の解析テストです。` 调用 tokenizer
- **THEN** 结果包含 surface 依次为 日本/語/の/解析/テスト/です/。
- **AND** 每个 token 都暴露指向未修改输入的非负起始偏移

#### Scenario: 分词结果保留词典和读音字段
- **WHEN** tokenizer 处理 `買った` 中的活用动词
- **THEN** surface 为 `買っ` 的 token 暴露 UniDic lemma `買う`
- **AND** 该 token 的书写基本形、表面形读音、基本形读音和发音作为独立字段存在

#### Scenario: 分词结果保留四级词性和活用字段
- **WHEN** tokenizer 处理包含形态学详情的 token
- **THEN** token 以稳定位置暴露 pos1、pos2、pos3 和 pos4
- **AND** token 暴露活用类型和活用形，后续字段不发生错位

#### Scenario: 分词结果暴露规范化音调
- **WHEN** tokenizer 处理 `aType` 为 `2` 这样的有效 UniDic token
- **THEN** token 暴露原始 `aType` `2` 和规范化音调核 `2`

#### Scenario: 保留多值音调字段
- **WHEN** UniDic 节点的 `aType` 由带引号的 CSV 或多个逗号分隔候选值表示
- **THEN** token 将完整解码后的 `aType` 作为一个原始字段暴露
- **AND** 规范化音调核使用第一个合法候选，同时 lemma、读音、词性和后续 UniDic 字段保持对齐

#### Scenario: 非法音调保持未知
- **WHEN** UniDic 节点的 `aType` 为空、为 `*`、格式错误或超出读音拍数
- **THEN** token 保留原始字段，但不暴露规范化音调核

#### Scenario: 原始音调不是最终音调结果
- **WHEN** token 暴露原始 UniDic `aType`
- **THEN** tokenizer 不得声称该值包含 Doki 人工覆盖、Kanjium 数据或最终音调选择规则

#### Scenario: 保留固定词典的分词结果
- **WHEN** tokenizer 使用固定的 `unidic-py` `3.1.0+2021-08-31` 资源处理 `アルミホイルを買った`
- **THEN** `アルミホイル` 被拆成两个短单元 `アルミ` 和 `ホイル`

#### Scenario: 空输入不抛异常
- **WHEN** 调用方传入 null、空字符串或仅包含空白的文本
- **THEN** tokenizer 返回空 token 列表且不抛异常

### Requirement: Preserve source character offsets
The system SHALL report each token start offset as a zero-based UTF-16 code-unit index into the original .NET input string. Whitespace skipped by MeCab SHALL still contribute to subsequent offsets.

#### Scenario: Offsets include spaces and line breaks
- **WHEN** the tokenizer processes the input "  日本\n語"
- **THEN** the token "日本" has start offset 2
- **AND** the following token "語" has start offset 5

#### Scenario: Token surface matches its source span
- **WHEN** the tokenizer returns a token for non-empty input
- **THEN** slicing the original input at the token start offset for the token surface length returns the same surface

### Requirement: Deterministic concurrent tokenization
The system SHALL allow concurrent callers to use the registered tokenizer singleton without corrupting results or leaking state between requests.

#### Scenario: Concurrent calls return stable results
- **WHEN** multiple threads tokenize different golden-corpus sentences through the same tokenizer instance
- **THEN** each result equals the result produced for that sentence by an isolated call
- **AND** no call throws because another parse is in progress

### Requirement: Validate dictionary availability
The system SHALL distinguish a missing dictionary from an installed dictionary whose version, format, or integrity metadata is invalid.

#### Scenario: Required runtime file is missing
- **WHEN** the selected dictionary directory lacks any of `char.bin`, `matrix.bin`, `sys.dic`, or `unk.dic`
- **THEN** the tokenizer throws a user-facing exception carrying the `UniDicDictionaryMissing` error code

#### Scenario: Optional files are absent
- **WHEN** all four required runtime files are valid but `uni.dic` or `model.bin` is absent
- **THEN** dictionary validation succeeds for the LibNMeCab 0.10.2 runtime path

#### Scenario: Installed dictionary metadata is invalid
- **WHEN** runtime files exist but the installed manifest, expected version, `unidic22` format, or recorded integrity information does not match the pinned asset
- **THEN** the tokenizer throws a user-facing exception carrying the `UniDicDictionaryInvalid` error code

### Requirement: Install a pinned dictionary asset
The system SHALL install the Doki-compatible `unidic-py` dictionary build `3.1.0+2021-08-31` from a fixed URL or a local offline archive. Both paths SHALL verify the expected SHA-256 from the checked-in manifest before making the dictionary available.

#### Scenario: First online installation
- **WHEN** the dictionary is not installed and a caller invokes online installation
- **THEN** the installer downloads the fixed dictionary URL without resolving a `latest` alias
- **AND** it verifies SHA-256, version, `unidic22` format, and the four required runtime files before atomically installing the dictionary under the local cache directory

#### Scenario: Application startup does not wait for installation
- **WHEN** the application starts without an installed default dictionary
- **THEN** installation is triggered through an observed background operation without blocking the main window
- **AND** installation failure is logged without producing an unobserved task exception

#### Scenario: Already installed
- **WHEN** the expected runtime files and installed manifest are already valid
- **THEN** the installer returns without downloading or replacing the dictionary

#### Scenario: Checksum or format mismatch
- **WHEN** a downloaded or offline archive does not match the expected SHA-256, version, or `unidic22` format
- **THEN** the installer throws a user-facing exception carrying the `UniDicDictionaryInvalid` error code
- **AND** the invalid archive is not promoted to the active dictionary directory

#### Scenario: Network or extraction failure
- **WHEN** download or extraction fails for an I/O reason
- **THEN** the installer throws a user-facing exception carrying the `UniDicDownloadFailed` error code
- **AND** partial temporary files are cleaned up so a retry can succeed

#### Scenario: Cancel installation
- **WHEN** installation is cancelled before the atomic promotion step
- **THEN** cancellation is propagated to the caller
- **AND** the previously installed dictionary, if any, remains usable
- **AND** staging files are cleaned up

#### Scenario: Install from an offline archive
- **WHEN** a caller selects a local archive matching the pinned manifest
- **THEN** the installer applies the same hash, version, format, file-set, staging, and atomic-promotion checks used by online installation
- **AND** installation succeeds without network access

#### Scenario: Concurrent installers
- **WHEN** multiple application processes attempt to install the same dictionary concurrently
- **THEN** only one process promotes the active dictionary directory at a time
- **AND** all processes observe either the previous valid installation or the completed new installation, never a partial directory

### Requirement: Dictionary directory override
The system SHALL allow overriding the tokenizer dictionary location via the `KOTOBA_UNIDIC_DIR` environment variable for development and testing.

#### Scenario: Valid environment override
- **WHEN** `KOTOBA_UNIDIC_DIR` points to a dictionary directory containing the four required runtime files and compatible version/`unidic22` metadata
- **THEN** the tokenizer loads that directory instead of the default cache path
- **AND** a project-generated installed manifest is not required for this explicit development/test override

#### Scenario: Invalid environment override
- **WHEN** `KOTOBA_UNIDIC_DIR` is set but the selected directory is missing or invalid
- **THEN** the tokenizer reports the corresponding missing or invalid dictionary error
- **AND** it does not silently fall back to a different dictionary

### Requirement: Release provenance and license evidence
The release process SHALL preserve separate provenance and license evidence for the LibNMeCab code dependency and the UniDic data asset.

#### Scenario: Verify a distributable build
- **WHEN** a release build containing the tokenizer is prepared
- **THEN** it includes the pinned LibNMeCab version and package hash, dictionary version/source/hash manifest, applicable LibNMeCab GPL/LGPL license texts, and UniDic BSD notice
- **AND** the release records the selected LGPL compliance path for single-file, trimming, AOT, or embedded-assembly packaging
