# staged-overlay-analysis Specification

## Purpose
TBD - created by archiving change defer-overlay-underlines-until-ai. Update Purpose after archive.
## Requirements
### Requirement: Stage local furigana before cloud analysis
For initial or full-change recognition, the application SHALL publish a local overlay containing UniDic-resolved words and furigana immediately after local grouping, without waiting for provider I/O. Cloud phrase analysis SHALL be required; historical enable settings SHALL NOT bypass the analysis workflow. The staged overlay SHALL contain no underline elements or LLM meanings. Publication SHALL update the saved session while target visibility rules determine whether it is displayed; temporary hiding due to target invisibility, minimization, or an occlusion union strictly greater than 30% of the full client area SHALL retain the latest completed result for restoration. 在增量识别场景中，系统 SHALL 在增量模式完成前保持上一轮 overlay 不变（未变化部分的下划线、短语组、词义均保留），增量模式完成后再刷新。

#### Scenario: Initial overlay appears before a slow provider
- **WHEN** initial or full-change local recognition and grouping finish, the provider has not returned yet, and the target is renderable
- **THEN** the overlay is visible with furigana for eligible words and without word underlines or LLM meanings

#### Scenario: Historical disabled-analysis setting does not bypass analysis
- **WHEN** a historical setting disables phrase analysis
- **THEN** the application still enters cloud analysis and publishes only local furigana while waiting, without successful-analysis underlines

#### Scenario: Incremental recognition preserves prior overlay during analysis
- **WHEN** incremental recognition is in progress (some segments reuse cached LLM results, others need fresh analysis)
- **THEN** the prior overlay session SHALL NOT be cleared; unchanged word underlines, phrase groups, and word meanings SHALL remain visible while the target is renderable until the incremental cycle completes

#### Scenario: Incremental cycle completion refreshes overlay once
- **WHEN** all incremental segments (both cached and fresh) are processed
- **THEN** the application SHALL issue exactly one overlay refresh containing the merged result

### Requirement: Publish one post-analysis overlay refresh
After all sentence requests in an analysis batch complete, the application SHALL replace the staged overlay exactly once. The replacement SHALL add underlines and validated meanings for successful sentence segments, preserve furigana for every local word, and retain a warning for failed segments. 在增量模式下，替换 SHALL 保留上一轮缓存命中段的下划线和短语组，仅新增或更新有变化的段。

#### Scenario: All requests succeed
- **WHEN** every sentence request completes successfully
- **THEN** one replacement overlay contains furigana, underlines, phrase groups, and validated word meanings

#### Scenario: Some requests fail
- **WHEN** at least one sentence request succeeds and another fails
- **THEN** one replacement overlay underlines and annotates only words from successful segments, leaves failed-segment words with furigana only, and exposes a retryable warning

#### Scenario: Every request fails
- **WHEN** all sentence requests fail, time out, are refused, or return malformed data
- **THEN** the overlay remains furigana-only and exposes a retryable warning without rendering invalid groups or meanings

#### Scenario: Incremental merge preserves cached segments
- **WHEN** incremental analysis produces fresh results for some segments and reuses cached results for others
- **THEN** the replacement overlay SHALL contain underlines/groups from both fresh and cached segments, SHALL NOT clear the cached segments

### Requirement: Superseded recognition cannot publish
系统 SHALL 在较新的识别、目标切换、用户明确停止或主动隐藏并暂停后，拒绝旧任务返回的本地或云端结果。停止操作 SHALL 清除可恢复结果，主动隐藏并暂停 SHALL 保留已完成结果及有效缓存；两者都 SHALL 取消未完成任务并阻止后续模型请求和重试。只有目标因原有隐藏、最小化、遮挡规则暂时不能显示、且用户没有主动隐藏时，系统才 SHALL 允许当前有效任务更新保存结果。用户主动恢复阅读时 SHALL 检查当前画面，优先使用有效缓存，仅对缺少分析的句段请求模型；窗口自身恢复显示仍沿用原有不主动发起识别的规则。

#### Scenario: 旧分析晚于新识别返回
- **WHEN** 第二次识别已经开始，第一次云端分析才返回
- **THEN** 第一次返回的结果不能替换第二次识别的结果

#### Scenario: 原隐藏入口停止识别后旧分析返回
- **WHEN** 用户通过原隐藏入口对应的“停止识别并清除标注”操作结束会话，但云端分析尚未完成
- **THEN** 晚到结果不更新保存内容，不重新显示标注，也不重启自动识别

#### Scenario: 窗口暂时不能显示时保留当前分析
- **WHEN** 当前分析尚未完成，目标变为不可见、最小化或被遮挡的合并面积超过客户区的 30%
- **THEN** 完成结果只更新保存会话，不显示标注；窗口恢复且用户没有主动隐藏后才可展示，恢复本身不调用识别或模型

#### Scenario: 新的完整识别替代旧增量识别
- **WHEN** 增量识别尚未结束，新的完整识别已经开始
- **THEN** 系统不再接收旧增量任务的结果，只保留当前完整识别的有效结果

#### Scenario: 用户主动隐藏时分析晚到
- **WHEN** 用户隐藏并暂停后，隐藏前的分析任务仍返回了结果
- **THEN** 系统忽略该结果，保留隐藏前已完成的内容，不显示标注、不发起后续分析，也不启动自动检测

#### Scenario: 隐藏时仍有排队的句段或重试
- **WHEN** 一轮分析已有请求正在执行，还有其他句段尚未发送或准备重试，用户隐藏并暂停
- **THEN** 系统取消未完成任务，隐藏后不再发送排队句段或重试请求

#### Scenario: 恢复后旧分析才返回
- **WHEN** 用户隐藏并暂停，又恢复并开始了新的本地检查，隐藏前的旧分析才返回
- **THEN** 旧结果仍被忽略，不能覆盖恢复后的结果，也不能补发后续请求

#### Scenario: 恢复时优先使用有效缓存
- **WHEN** 当前画面检查完成，部分句段在隐藏前已有成功缓存，另一些句段没有完整分析
- **THEN** 系统复用有效缓存并只请求缺少分析的句段，不因恢复动作清空缓存，不因整段原文相同就跳过缺失分析

### Requirement: 保存语言不立即替换已有显示

保存释义语言时，系统 SHALL 保留已经显示或因目标临时隐藏而保存的词义、短语组和弹窗状态，下一次识别时按现有流程更新。语言设置 SHALL 不主动触发 OCR、模型请求或新的覆盖层发布；已有目标可见性、增量合并和识别发布规则继续适用。

#### Scenario: 保存后保留当前结果
- **WHEN** 当前显示中文释义，用户保存“法语”但尚未进行新识别
- **THEN** 当前显示继续保留，之后启动的分析使用法语设置，保存本身不触发请求或覆盖层重建

### Requirement: 换语言后下一次识别不被相同原文跳过

有效语言变化时，系统 SHALL 使仅按原文判断“无需分析”的旧状态失效。下一次真正启动的手动或自动识别 SHALL 使用新语言，即使其 OCR 原文与上次完全相同；没有启动新识别时 SHALL 不主动发起 OCR 或模型调用。

#### Scenario: 原文相同仍按新语言分析
- **WHEN** 用户保存法语后，下一次识别得到与上次完全相同的日语原文
- **THEN** 系统不因原文相同而跳过分析，按法语构建新请求

### Requirement: 整句译文随本轮结果一起更新

系统 SHALL 在现有分析完成后的覆盖层更新中一并发布对应句段的原文，以及本次要求并获得的译文，不为译文增加第二轮刷新。入口是否显示 SHALL 同时服从当前开关，关闭时不得重新显示入口。第一次识别等待模型时 SHALL 保持原有本地预览。增量识别等待期间 SHALL 保留上一轮结果；完成后一起替换，关闭旧详情弹窗并恢复折叠。失败句段 SHALL 不借用上一轮译文。

#### Scenario: 首次分析完成
- **WHEN** 本次要求译文，且本轮词义、语法和译文均已返回
- **THEN** 它们随同一次完成更新发布，译文保持折叠，不自动弹出

#### Scenario: 增量识别尚未完成
- **WHEN** 新增台词正在分析，部分旧句段命中缓存
- **THEN** 继续保留上一轮完整内容，直到本轮结束后一次替换，不把新原文和旧译文混搭

#### Scenario: 部分句段失败
- **WHEN** 一段分析成功，另一段失败
- **THEN** 成功段可以查看本轮译文，失败段不显示旧译文，其他本地信息按现有规则保留

### Requirement: 隐藏和停止同样约束整句译文

整句结果 SHALL 遵守现有识别任务的有效性和可见性规则。较新的识别、目标切换或停止识别之后，旧任务 SHALL 不得替换整句结果或重新打开弹窗。目标暂时隐藏、最小化、遮挡或用户主动临时隐藏时，仍有效的分析 SHALL 只更新保存结果，不显示弹窗；恢复后 SHALL 从折叠状态按需查看。此要求不新增隐藏操作，也不改变 `add-reading-keyboard-shortcuts` 对临时隐藏和停止的区分。

#### Scenario: 停止后旧译文晚到
- **WHEN** 用户停止识别并清除结果，未完成的模型请求随后返回译文
- **THEN** 该译文不进入当前显示结果，也不重新打开弹窗

#### Scenario: 隐藏期间完成分析
- **WHEN** 当前分析仍有效，但目标或标注暂时隐藏
- **THEN** 只保存新结果，不显示译文；恢复后也不自动展开，恢复本身不调用 OCR 或模型

#### Scenario: 切换目标后收到旧响应
- **WHEN** 用户已经换到另一个目标窗口，原目标的译文才返回
- **THEN** 原目标译文不能显示在新目标的弹窗中

### Requirement: 开关切换不运行识别但使旧跳过判断失效

保存整句翻译开关 SHALL 只更新有效设置和译文区可见性，不主动执行 OCR、调用模型、重做当前词义或更改自动识别状态。下一次真正开始的手动或自动识别 SHALL 使用新值，即使原文与上次相同，也不能使用旧开关值跳过分析。已发出的请求 SHALL 继续按原值处理，结果显示服从当前设置。

#### Scenario: 保存开关但没有识别
- **WHEN** 用户切换开关且没有开始新识别，画面也没有触发自动识别
- **THEN** 不增加 OCR 或模型请求，当前词义和语法保留，只调整译文入口及展开区

#### Scenario: 下一次原文相同
- **WHEN** 上次识别关闭翻译，用户开启后再次真正启动识别，OCR 原文未变化
- **THEN** 使用开启状态查询缓存或分析，不因原文相同直接跳过
