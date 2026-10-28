# 词典悬停弹窗使用目标语言释义

## RENAMED Requirements

- FROM: `### Requirement: Dictionary popup on hover`
- TO: `### Requirement: 词典悬停弹窗使用模型目标语言释义`

## MODIFIED Requirements

### Requirement: 词典悬停弹窗使用模型目标语言释义

系统 SHALL 在覆盖层悬停本地合并词时展示词头、词性、读音、模型最佳释义和语法说明，并将弹窗定位在词的包围盒下方且不遮挡词。词头、读音和活用型 SHALL 继续由本地 JMdict 与 UniDic 提供；语境词性、释义及语法 SHALL 遵守 `llm-word-meanings` 的约定，其中解释字段使用对应请求的目标语言。JMdict 的英文释义 SHALL 不重新成为弹窗的解释来源。

#### Scenario: 悬停后显示目标语言解释
- **WHEN** 鼠标悬停在已有本地合并结果和有效模型词项的词上
- **THEN** 弹窗显示词头、语境词性、读音及模型返回的目标语言释义和语法说明

#### Scenario: 无有效词项时保留最小内容
- **WHEN** 悬停词缺少词典条目或模型词项
- **THEN** 弹窗按现有回退规则显示本地读音及界面语言的“未收录／无释义”提示，不为空白或崩溃，也不改用本地英文释义
