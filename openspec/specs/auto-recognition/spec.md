# auto-recognition Specification

## Purpose

Detect changes in the selected recognition region and run full or incremental recognition while reusing sentence analysis results.

## Requirements

### Requirement: 自动识别由设置开关控制
系统 SHALL 提供布尔设置项 `AutoRecognitionEnabled`，默认关闭，用于保存自动识别偏好。未处于主动隐藏暂停状态时，用户通过界面开关或快捷键主动开启，如果已有可用目标且没有会话，系统 SHALL 先识别一次，再开始自动检测；已有会话时 SHALL 直接开始自动检测；正在执行手动识别时 SHALL 等它结束，不重复启动。没有可用目标时 SHALL 只保存偏好并提示先选择或恢复目标。读取启动设置、仅选择目标和预览重试 SHALL NOT 自动发起识别。关闭自动识别 SHALL 停止采样并取消尚未完成的自动任务，保留最后已显示结果和当前会话，不取消独立的手动识别。用户主动隐藏时 SHALL 停止自动检测并取消未完成的手动和自动任务，保留偏好和已完成结果；隐藏期间切换开关 SHALL 只保存偏好，恢复阅读成功后才按当前偏好决定是否自动检测。

#### Scenario: 设置开关默认关闭
- **WHEN** 用户首次使用应用且未修改 `AutoRecognitionEnabled` 设置
- **THEN** 系统不运行自动识别，用户可以请求一次识别，或者主动开启自动识别

#### Scenario: 用户开启自动识别
- **WHEN** 用户通过界面开关或快捷键主动开启自动识别，已有有效会话且未主动隐藏暂停
- **THEN** 系统启动自动检测，保留当前结果

#### Scenario: 用户关闭自动识别
- **WHEN** 用户通过界面开关或快捷键关闭自动识别
- **THEN** 系统停止采样、取消未完成的自动任务，保留最后已显示结果和当前会话，不让旧自动任务之后覆盖它

#### Scenario: 设置在应用重启后生效
- **WHEN** 应用启动时自动识别偏好为开启
- **THEN** 系统不立即识别，用户选择目标并显式开始识别后，首次识别成功才启动自动检测

#### Scenario: 已有目标但尚未识别时开启
- **WHEN** 已有可用目标但没有识别会话，用户把自动识别从关闭切换为开启，且未主动隐藏暂停
- **THEN** 系统立即识别一次，成功后开始自动检测，首次识别失败则显示现有错误提示且不运行自动检测

#### Scenario: 没有可用目标时开启
- **WHEN** 用户开启自动识别，但没有目标或目标不可用
- **THEN** 系统保存开启偏好并提示先选择或恢复目标，不自动选择窗口，不开始识别

#### Scenario: 仅选择目标不启动识别
- **WHEN** 自动识别偏好已经开启，用户随后只完成目标选择或预览重试
- **THEN** 系统仍等待显式的开始识别请求，不因为选好了窗口就调用识别或模型

#### Scenario: 手动识别中开启或暂停
- **WHEN** 一次手动识别正在执行，用户切换自动识别开关
- **THEN** 系统只更新偏好，不重复启动或取消这次手动识别，手动识别结束后按最新开关状态及是否主动隐藏决定是否自动检测

#### Scenario: 主动隐藏优先于自动识别偏好
- **WHEN** 自动识别偏好为开启，用户隐藏并暂停阅读辅助
- **THEN** 自动检测停止，偏好保持开启，旧任务完成或目标恢复可见都不能重启检测

#### Scenario: 隐藏期间修改偏好
- **WHEN** 用户在主动隐藏暂停期间切换自动识别开关
- **THEN** 系统只保存偏好，不执行捕获、识别或模型请求；用户之后恢复阅读成功，才按最新偏好决定是否继续自动检测

### Requirement: Empty-result region warnings are manual-only
The system SHALL show the red neon region warning only when a completed, current manual recognition request returns no recognized words. Enabling automatic recognition SHALL NOT suppress this manual feedback. Automatic empty results SHALL NOT start or restart the warning, including repeated empty results or empty results after successful recognition. Automatic results containing words SHALL stop any active region warning.

#### Scenario: Manual recognition finds no words
- **WHEN** the user clicks the recognition button and the current request completes without recognized words
- **THEN** the warning appears for the saved region, or the full target client area when no region is saved, even if automatic recognition is enabled
- **AND** each subsequent manual empty recognition can show the warning again

#### Scenario: Automatic recognition finds no words
- **WHEN** an automatic recognition callback completes without recognized words
- **THEN** it does not show or restart the red border or its warning message

#### Scenario: Automatic recognition finds words
- **WHEN** an active automatic recognition callback returns recognized words
- **THEN** any warning from a preceding manual empty recognition stops

### Requirement: 像素差异变化检测
系统 SHALL 以可配置的间隔（默认 300ms）从当前选中 HWND 的 WGC 会话读取识别区域，并与上一次触发 OCR 的可比较样本进行像素级差异比较，使低于阈值的小变化能够累计。当差异超过阈值时，系统 SHALL 触发 OCR 文本稳定确认。差异采样和后续 OCR SHALL 使用同一目标的 WGC 画面，不依赖桌面屏幕拷贝，也 SHALL NOT 被 WGC 到帧事件直接驱动。遮挡和失去前台焦点 SHALL NOT 暂停检测；目标隐藏、最小化、关闭或捕获不可用时 SHALL 暂停提供有效样本，失败后按当前轮询间隔有限重试，不执行无界内部重试。

#### Scenario: 无变化时不触发识别
- **WHEN** 连续两份可比较样本的像素差异低于阈值
- **THEN** 系统 SHALL 不启动新的 OCR 确认；已经开始的文字稳定确认 SHALL 继续按自己的采样间隔执行

#### Scenario: 有变化时触发 OCR 确认
- **WHEN** 连续两份可比较样本的像素差异超过阈值
- **THEN** 系统 SHALL 从同一目标会话读取当前有效区域画面执行 OCR，并将 OCR 结果与上一次已识别的文本进行对比

#### Scenario: OCR 确认文本未变化时跳过
- **WHEN** 像素差异超阈值但 OCR 文本结果与上次完全相同
- **THEN** 系统 SHALL 不触发 LLM 分析和 overlay 更新，保持当前状态

#### Scenario: OCR 确认文本已变化时启动识别
- **WHEN** 像素差异超阈值且 OCR 文本结果与上次不同
- **THEN** 系统 SHALL 启动新一轮识别流程（全变或增量，取决于文本关系）；合并遮挡比例超过客户区面积的 30% 时只更新保存的会话，不显示浮层，30% 及以下仍可显示

#### Scenario: 目标窗口不可用时暂停检测
- **WHEN** 目标窗口被隐藏、最小化或关闭，或当前 WGC 会话不能提供有效样本
- **THEN** 系统 SHALL 不对不可用缓存执行 OCR，目标与捕获恢复可用后重新建立比较基线；关闭后的旧目标不得自动替换为其他 HWND

#### Scenario: 遮挡期间继续检测
- **WHEN** 已启用自动识别且目标被部分或完全遮挡，但仍可捕获并继续产生画面
- **THEN** 系统 SHALL 继续读取该目标区域、比较像素并按文本变化运行识别，遮挡物自身的变化不进入样本

#### Scenario: Captured pixels remain static
- **WHEN** 当前代次有效但目标没有产生变化，或反复返回相同像素
- **THEN** 系统 SHALL 按无变化处理，不强制运行 OCR 或 LLM，也不将重复像素报告为捕获失败

### Requirement: 全变模式识别
当新旧 OCR 文本之间没有包含关系时，系统 SHALL 按句段分别检查缓存，保留仍然匹配的结果。自动识别 SHALL NOT 因一个句段改字而清空全部 LLM 缓存。稳定确认完成后，系统 SHALL 移除已消失内容的标注，为缺少分析的句段显示本地预览，并保留未变化句段的有效标注。

#### Scenario: 文本完全不同
- **WHEN** 新 OCR 文本与旧文本无包含关系（非子串/超集）
- **THEN** 系统 SHALL 在文字稳定后替换已经消失的内容，复用仍匹配的句段缓存，仅为缺少分析的句段请求模型

#### Scenario: 一个句段改字时保留其他缓存
- **WHEN** 一个新句段的 OCR 被修正，其他句段文字未变化
- **THEN** 系统 SHALL 保留未变化句段的缓存、振假名和下划线，不因全文包含关系改变而重新分析这些句段

### Requirement: 增量模式识别
当新 OCR 文本包含旧文本时，系统 SHALL 执行增量模式：保留旧 overlay 中未变化部分，仅对新内容执行 LLM 分析。

#### Scenario: 追加新台词
- **WHEN** 旧文本"今日はいい天気ですね"存在，新文本为"今日はいい天気ですね。散歩に行きましょう"
- **THEN** 系统 SHALL 检测到旧文本是新文本的前缀，以增量模式处理

#### Scenario: 增量模式合并文本后分析
- **WHEN** 进入增量模式
- **THEN** 系统 SHALL 将完整的新文本按句子分段，对每个分段检查 LLM 缓存

#### Scenario: 缓存命中跳过 LLM
- **WHEN** 分段文本"今日はいい天気ですね"在 LLM 缓存中存在
- **THEN** 系统 SHALL 直接使用缓存结果，SHALL NOT 发送 LLM 请求

#### Scenario: OCR adds or removes a boundary dialogue quote
- **WHEN** an OCR segment differs only by leading `「`/`『` or trailing `」`/`』`, and its content tokens retain identical lexical metadata and relative offsets
- **THEN** the system SHALL reuse its cached or matching in-flight analysis, keep its group identity, and rebind token references to the current geometry
- **AND** current source text SHALL retain the currently recognized quotation marks; changed words, internal quotes, or other punctuation SHALL NOT be normalized into a cache hit

#### Scenario: Closing dialogue quotes follow terminal punctuation
- **WHEN** a line ends with sentence-final punctuation followed by closing quotes, including an ASCII `?` or `!`
- **THEN** the next line SHALL begin a separate segment, so appending a new dialogue does not replace the preceding segment's cache key
- **AND** a closing quote without preceding terminal punctuation SHALL NOT by itself split a wrapped sentence

#### Scenario: 缓存未命中调用 LLM
- **WHEN** 分段文本"散歩に行きましょう"不在 LLM 缓存中
- **THEN** 系统 SHALL 发送 LLM 请求分析该分段，并将结果写入缓存

#### Scenario: 增量模式合并渲染
- **WHEN** 所有分段处理完成（缓存命中 + 新 LLM 结果）
- **THEN** 系统 SHALL 合并所有分段的结果，刷新 overlay 显示完整内容

### Requirement: 轮询间隔可配置
系统 SHALL 支持配置自动识别的轮询间隔（毫秒），默认值为 300ms，最小值 SHALL 不低于 100ms。

#### Scenario: 使用默认轮询间隔
- **WHEN** 未配置轮询间隔
- **THEN** 系统 SHALL 使用 300ms 作为轮询间隔

#### Scenario: 配置自定义轮询间隔
- **WHEN** 用户将轮询间隔设为 500ms
- **THEN** 系统 SHALL 以 500ms 间隔进行截屏检测

#### Scenario: 轮询间隔下限保护
- **WHEN** 用户尝试将轮询间隔设为低于 100ms
- **THEN** 系统 SHALL 将实际间隔钳制为 100ms

### Requirement: Reset incompatible sampling baselines
系统 SHALL 在目标改变、捕获会话代次失效、客户区尺寸或采样区域改变、隐藏/最小化及捕获失败后使旧差异基线失效。恢复后的第一份有效样本 SHALL 建立新基线而不单凭重新初始化触发 OCR；只有后续可比较样本的变化才触发 OCR 文本确认。单纯遮挡或失焦 SHALL 保留当前基线。现有默认关闭、首次手动识别后启动、最小 100ms 间隔、关闭开关停止轮询及句子缓存规则 SHALL 保持不变。

#### Scenario: Establish a baseline after capture restarts
- **WHEN** 暂停后的 WGC 会话重新提供第一份有效区域样本
- **THEN** 系统仅建立新比较基线，不将旧尺寸、旧区域或旧代次差异当成新台词

#### Scenario: Keep the baseline across occlusion
- **WHEN** 只有遮挡或前台状态发生变化，目标、区域和捕获代次未改变
- **THEN** 系统保留可比较的基线，后续目标文字变化仍可正常触发自动识别

#### Scenario: Retry a failed automatic sample
- **WHEN** 一次自动采样发生可重试的 WGC 捕获失败或超时
- **THEN** 系统记录英文错误并清理失效基线，不进行 OCR、不重复弹窗，并在后续采样机会重新检查目标及捕获状态

### Requirement: Confirm stable OCR text before cloud analysis

Manual recognition, automatic recognition, and resume SHALL confirm locally recognized text for at least 600 ms before submitting the confirmed snapshot for phrase analysis. Local confirmation SHALL sample at 200 ms intervals, restart the confirmation window whenever text changes, and retain the latest sample geometry. Pixel differences SHALL NOT gate samples within confirmation. Background-only changes SHALL NOT restart the text confirmation window. Existing annotations SHALL remain during automatic confirmation. Continuously changing text SHALL wait until stable or cancelled, without a timeout that forces a partial sentence to the provider.

#### Scenario: Typewriter text becomes complete
- **WHEN** OCR first returns a partial line and subsequent samples return its completed text
- **THEN** only the completed snapshot that remains unchanged for 600 ms is submitted for analysis

#### Scenario: Pause while confirming text
- **WHEN** the user stops, hides, or disables automatic recognition during automatic confirmation
- **THEN** confirmation is cancelled and its partial text cannot trigger a model request

### Requirement: Continue local observation during provider requests

Automatic observation SHALL finish after local confirmation and scheduling rather than awaiting provider I/O. The application SHALL keep one active publication batch and at most one waiting snapshot, replacing an obsolete waiting snapshot with the newest confirmed one. Superseded snapshots SHALL NOT publish late results. Matching in-flight sentence requests SHALL be shared; total concurrent provider requests SHALL remain bounded at four across overlapping batches. Stopping SHALL cancel and await tracked automatic observation and publication work.

#### Scenario: More text appears while a provider is slow
- **WHEN** a provider batch is pending and multiple newer snapshots become stable
- **THEN** local observation continues, only the newest waiting snapshot is retained, and the current sentence cache is reused when that snapshot is processed

#### Scenario: Duplicate sentences share an in-flight request
- **WHEN** matching text, tokenization, explanation language, and translation mode are requested before an existing request completes
- **THEN** the application reuses that request and rebinds its token ids to the current layout without sending another request
