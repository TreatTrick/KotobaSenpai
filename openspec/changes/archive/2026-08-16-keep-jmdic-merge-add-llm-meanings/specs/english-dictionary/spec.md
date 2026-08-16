# english-dictionary Specification (delta)

## MODIFIED Requirements

### Requirement: Dictionary popup on hover
系统 SHALL 在覆盖层悬停某个本地合并词时，弹出释义小窗，显示`头词 + [词性] + [读音] + LLM 最佳中文释义 + 语法`，并定位在该词包围盒下方且不遮挡该词。头词、读音与活用型由本地 jmdic/UniDic 提供，语境词性、最佳中文释义与语法来自 `llm-word-meanings`。本地 JMdict 的英文释义不再作为弹窗语义来源。

#### Scenario: Show popup on hover
- **WHEN** 鼠标悬停在某个有本地合并词的词上
- **THEN** 系统弹出释义窗，展示头词、语境词性、读音与 LLM 返回的最佳中文释义与语法

#### Scenario: No entry shows minimal content
- **WHEN** 悬停词无词典条目或 LLM 词条
- **THEN** 系统展示该词的读音与"未收录/无释义"提示，而不是空白或崩溃