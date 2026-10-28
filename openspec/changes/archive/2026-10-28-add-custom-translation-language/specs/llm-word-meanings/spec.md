# 词义和悬停释义使用目标语言

## RENAMED Requirements

- FROM: `### Requirement: LLM returns per-word contextual meaning`
- TO: `### Requirement: 为每个本地词返回目标语言释义`
- FROM: `### Requirement: Word meaning drives the hover popup`
- TO: `### Requirement: 悬停弹窗显示目标语言词义`

## MODIFIED Requirements

### Requirement: 为每个本地词返回目标语言释义

系统 SHALL 在现有句段分析调用中，除 `groups[]` 外返回平行的 `words[]`，覆盖请求内 `LocalSpans` 的每个本地合并词，包括助词和助动词。每项 SHALL 用 `headword` 逐字复制对应词块的原文，并按请求的有效语言生成该词在语境中的单一最佳 `meaning` 和简洁 `grammar`。`pos` SHALL 保留现有日语词性约定。词项 SHALL 不返回词元标识或读音，不引用请求之外的实体；这些本地信息继续由词典和 UniDic 提供。

#### Scenario: 覆盖全部本地词块
- **WHEN** 句段包含学校、は、を等本地词块，包括助词或助动词
- **THEN** 每个词块各有一项词级记录，不挑选、不遗漏，解释字段统一要求当前目标语言

#### Scenario: 合并词按整体原文引用
- **WHEN** 本地把多个词元合并为 `でも` 等词块
- **THEN** 词项以合并后的原文作为 `headword`，不拆成多项，也不重复本地读音和词元信息

### Requirement: 悬停弹窗显示目标语言词义

系统 SHALL 在悬停本地合并词时，展示词头、词性、读音、音调及可用的目标语言释义和语法说明。读音和音调 SHALL 继续来自本地 UniDic；音调使用现有统一记法，例如 `[2] LH↓L`。释义 SHALL 仅在对应句段完整 AI 分析成功并完成最终覆盖层刷新后可用。没有有效释义时 SHALL 展示本地词头、读音、已知音调和按界面语言显示的“无释义”提示；未知音调不得伪造。弹窗位置 SHALL 在词的包围盒下方且不遮挡词，并继续遵守 `phrase-popup-presentation` 的鼠标停留、10 DIP 离开距离及窗口内输入不透传规则。

#### Scenario: 成功后显示目标语言解释
- **WHEN** 法语分析成功并发布后，光标悬停在有词级记录的本地词上
- **THEN** 弹窗展示法语释义和语法说明，以及原有词头、词性、读音和音调；界面标题仍按界面语言显示

#### Scenario: 合并词完整展示读音和音调
- **WHEN** 悬停由多个 UniDic 词元合成且有释义的词
- **THEN** 弹窗按源词元顺序展示合并读音和各个音调片段，不只显示第一个词元

#### Scenario: 没有词义时使用本地信息
- **WHEN** 悬停词没有可用的模型词项
- **THEN** 弹窗显示词头、读音、已知音调和界面语言的“无释义”提示，不为空白或崩溃

#### Scenario: 等待模型时不提前显示释义
- **WHEN** 对应句段仍在等待模型
- **THEN** 本地振假名和音调继续可用，弹窗只使用本地信息回退

#### Scenario: 失败句段不显示模型释义
- **WHEN** 对应句段的模型分析失败
- **THEN** 弹窗保留本地词头、读音和已知音调，不显示该句段的模型释义

#### Scenario: 切换悬停目标不残留旧词
- **WHEN** 光标离开悬停词超过现有延迟，或切换到另一个词
- **THEN** 弹窗按原有规则隐藏或更新，不残留上一词的内容
