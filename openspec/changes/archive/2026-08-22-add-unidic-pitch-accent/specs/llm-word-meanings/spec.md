## MODIFIED Requirements

### Requirement: Word meaning drives the hover popup

系统 SHALL 在悬停某个本地合并词时，弹出释义小窗，展示该词的`词头 + [词性] + [读音] + [音调] + LLM 最佳中文释义 + 语法`，其中读音和音调来自本地 UniDic 音调片段，音调使用统一 notation（例如 `[2] LH↓L`）。并定位在该词包围盒下方且不遮挡该词。悬停的词若无 LLM 词条，弹窗 SHALL 展示该词的词头、读音、已知音调与“无释义”提示；音调未知时不得伪造红蓝模式。释义 SHALL 仅在对应句段的完整 AI 分析成功并完成最终覆盖层刷新后可用；等待期间的本地振假名和音调显示不得依赖释义结果。弹窗 SHALL 保持点击穿透，移出后延迟隐藏，切换时更新内容。

#### Scenario: Hover a word after successful analysis
- **WHEN** 光标悬停在完成 AI 分析且有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头、语境词性、振假名读音、音调 notation、LLM 最佳中文释义与语法说明

#### Scenario: Hover a merged word with meaning
- **WHEN** 光标悬停在有 LLM 词条且由多个 UniDic token 合并而成的本地词上
- **THEN** 弹窗按源 token 顺序展示合并读音和对应音调片段，不只显示第一个 token 的音调

#### Scenario: Hover a word with no meaning
- **WHEN** 光标悬停在没有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头、读音、已知音调，并提示“无释义”，而非空白或崩溃

#### Scenario: Hover during provider wait
- **WHEN** 光标悬停在 AI 仍在等待的本地词上
- **THEN** 本地振假名和音调标记可见，弹窗不展示尚未返回的 LLM 释义并使用本地词头/读音/音调回退

#### Scenario: Hover a failed sentence word
- **WHEN** 光标悬停在 AI 失败句段中的本地词上
- **THEN** 弹窗展示词头、读音和已知音调回退，不展示该句段的 LLM 释义

#### Scenario: Hide or update on hover change
- **WHEN** 光标移出悬停词并停留超过延迟时间，或切换到另一词
- **THEN** 弹窗隐藏，或内容更新为新词的读音、音调和可用释义，不残留旧词内容

### Requirement: Show per-word meanings within a phrase group

系统 SHALL 在选中/悬停一个语法组合组（`ParsedPhraseGroup`）并展示其详情时，除组级 label、释义与语法外，还逐词列出组内每个本地合并词（按组 token 归属）的`词头 + 词性 + 读音 + 音调 + LLM 释义`，以利于学习。组内无 LLM 词条的词 SHALL 显示词头、读音和已知音调作为回退；一个词由多个 UniDic token 构成时 SHALL 按源 token 顺序展示其音调片段。

#### Scenario: List words inside a group
- **WHEN** 一个组覆盖多个本地合并词（如 ご飯 を 食べた 组）
- **THEN** 组详情除整组 label/释义/语法外，还逐词列出词头、语境词性、读音、音调与 LLM 释义

#### Scenario: Fall back for a word without meaning
- **WHEN** 组内某个本地合并词没有 LLM 词条
- **THEN** 该词在组详情中显示词头、读音与已知音调，不显示伪造的释义或音调

### Requirement: Local fallback preserves underlines and readings

系统 SHALL 在 LLM 不可用（未配置 key、超时、拒绝、畸形 JSON 等）时，仍保留本地合并词的下划线、读音和可用的 UniDic 音调标记，弹窗仅展示词头、读音、音调与“无释义”，不崩溃、不隐藏本地结果。未知音调 SHALL 保持无标记状态，并不得阻止其他词显示已知音调。

#### Scenario: LLM unavailable keeps merged underlines and pitch
- **WHEN** 短语分析不可用
- **THEN** 本地合并词的下划线、振假名和已知红蓝音调标记仍全部可见，弹窗退化为仅显示词头、读音和音调

#### Scenario: No LLM meaning on offline
- **WHEN** 离线且无 LLM 释义
- **THEN** 弹窗不展示任何从 LLM 推导的释义，本地合并词的读音和音调结果保持可用
