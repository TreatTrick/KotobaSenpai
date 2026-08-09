## ADDED Requirements

### Requirement: Dictionary index loading
系统 SHALL 从随发布捆绑的 JMdict 索引文件加载本地日英词典，运行时不依赖网络。索引 SHALL 提供汉字表（表记 → 条目）与读音表（读音 → 条目）两种查找入口。

#### Scenario: Load bundled index
- **WHEN** 应用启动且捆绑的 JMdict 索引文件存在
- **THEN** 系统加载索引，可按表记或读音查词，加载失败不阻塞其他功能

#### Scenario: Index file missing
- **WHEN** 捆绑的 JMdict 索引文件缺失
- **THEN** 系统不崩溃，查词返回空结果并记录日志

### Requirement: Lookup words by token
系统 SHALL 依据分词的 lemma（辞书形）查词典，按 `Lemma → OrthBase → Reading → BaseReading` 顺序回退；命中读音时对片/平假名做归一转换后再查。

#### Scenario: Lookup by lemma
- **WHEN** token 的 lemma 存在于索引的表记表
- **THEN** 系统返回该词的词典条目

#### Scenario: Fall back to reading
- **WHEN** lemma 与 OrthBase 均未命中，但 token 的 reading 存在于读音表
- **THEN** 系统经片/平假名归一后按读音返回条目

#### Scenario: No entry found
- **WHEN** 所有回退键都未命中
- **THEN** 系统返回空结果，不抛未处理异常

### Requirement: Dictionary popup on hover
系统 SHALL 在覆盖层悬停某个分词词时，弹出本地词典释义小窗，显示`头词 + [读音] + 各义项(词性 + 英文释义)`，并定位在该词包围盒下方且不遮挡该词。

#### Scenario: Show popup on hover
- **WHEN** 鼠标悬停在某个有词典条目的分词词上
- **THEN** 系统弹出释义窗，展示头词、读音与各义项的英文释义

#### Scenario: No entry shows minimal content
- **WHEN** 悬停词无词典条目
- **THEN** 系统展示该词的读音与"未收录"提示，而不是空白或崩溃

### Requirement: Popup lifecycle and hit-through
弹窗 SHALL 保持非点击穿透（不拦截鼠标点击），在鼠标移出悬停词后（带短延迟防抖动）隐藏；悬停词切换时弹窗内容随之更新。

#### Scenario: Hide on leave
- **WHEN** 鼠标移出悬停词并停留超过延迟时间
- **THEN** 弹窗隐藏，且下方窗口点击不受影响

#### Scenario: Update on word switch
- **WHEN** 悬停切换到另一个词
- **THEN** 弹窗内容更新为新词的释义，不残留旧词内容