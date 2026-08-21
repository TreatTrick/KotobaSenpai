## MODIFIED Requirements

### Requirement: Word meaning drives the hover popup
系统 SHALL 在悬停某个本地合并词时，弹出释义小窗，展示该词的`词头 + [词性] + [读音] + LLM 最佳中文释义 + 语法`，并定位在该词包围盒下方且不遮挡该词。悬停的词若无 LLM 词条，弹窗 SHALL 展示该词的词头、读音与"无释义"提示。释义 SHALL 仅在对应句段的完整 AI 分析成功并完成最终覆盖层刷新后可用；等待期间的本地振假名显示不得依赖释义结果。弹窗 SHALL 保持点击穿透，移出后延迟隐藏，切换时更新内容。

#### Scenario: Hover a word after successful analysis
- **WHEN** 光标悬停在完成 AI 分析且有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头、语境词性、读音、LLM 最佳中文释义与语法说明

#### Scenario: Hover a merged word with meaning
- **WHEN** 光标悬停在有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头、语境词性、读音、LLM 最佳中文释义与语法说明

#### Scenario: Hover a word with no meaning
- **WHEN** 光标悬停在没有 LLM 词条的本地合并词上
- **THEN** 弹窗展示词头与读音并提示"无释义"，而非空白或崩溃

#### Scenario: Hover during provider wait
- **WHEN** 光标悬停在 AI 仍在等待的本地词上
- **THEN** 本地振假名可见，弹窗不展示尚未返回的 LLM 释义并使用无释义回退

#### Scenario: Hover a failed sentence word
- **WHEN** 光标悬停在 AI 失败句段中的本地词上
- **THEN** 弹窗展示词头与读音回退，不展示该句段的 LLM 释义

#### Scenario: Hide or update on hover change
- **WHEN** 光标移出悬停词并停留超过延迟时间，或切换到另一词
- **THEN** 弹窗隐藏，或内容更新为新词的可用释义，不残留旧词内容
