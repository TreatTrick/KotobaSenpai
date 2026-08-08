# japanese-tokenizer Specification

## Purpose
TBD - created by archiving change add-japanese-tokenizer. Update Purpose after archive.
## Requirements
### Requirement: Tokenize Japanese text
The system SHALL tokenize Japanese text into a sequence of tokens. Each token SHALL expose its surface, UniDic lemma, orthographic base (`orthBase`), surface-form reading (`kana`), base-form reading (`kanaBase`), pronunciation (`pron`), all four parts-of-speech levels, conjugation type, conjugation form, raw UniDic `aType`, and a start offset into the original input.

#### Scenario: Tokenize a simple sentence
- **WHEN** a caller invokes the tokenizer with Japanese text "日本語の解析テストです。"
- **THEN** the result contains tokens with surface forms 日本/語/の/解析/テスト/です/。
- **AND** each token exposes a non-negative start offset into the unchanged input text

#### Scenario: Tokenize reports dictionary and reading forms
- **WHEN** the tokenizer processes the conjugated verb in "買った"
- **THEN** the token with surface "買っ" reports the UniDic lemma "買う"
- **AND** its orthographic base, surface-form reading, base-form reading, and pronunciation are present as separate fields

#### Scenario: Tokenize reports all parts-of-speech and conjugation fields
- **WHEN** the tokenizer processes a token with morphological detail
- **THEN** the token exposes pos1, pos2, pos3, and pos4 in stable positions
- **AND** it exposes conjugation type and conjugation form without shifting fields

#### Scenario: Preserve the pinned dictionary segmentation
- **WHEN** the tokenizer using the pinned `unidic-py` `3.1.0+2021-08-31` asset processes "アルミホイルを買った"
- **THEN** "アルミホイル" is segmented into the two short units "アルミ" and "ホイル"

#### Scenario: Preserve a multi-valued accent field
- **WHEN** a UniDic node contains an `aType` value represented by quoted CSV or multiple comma-separated candidates
- **THEN** the token exposes the complete decoded `aType` value as one raw field
- **AND** lemma, reading, parts-of-speech, and following UniDic fields remain correctly aligned

#### Scenario: Raw accent is not a final pitch result
- **WHEN** a token exposes a raw UniDic `aType`
- **THEN** the tokenizer does not claim that value includes Doki manual overrides, Kanjium data, or final pitch-selection rules

#### Scenario: Empty or whitespace input
- **WHEN** a caller passes null, an empty string, or whitespace-only text
- **THEN** the tokenizer returns an empty token list without throwing

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

