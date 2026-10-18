## MODIFIED Requirements

### Requirement: 像素差异变化检测
系统 SHALL 以可配置的间隔（默认 300ms）从当前选中 HWND 的 WGC 会话读取识别区域，并与上一份可比较样本进行像素级差异比较。当差异超过阈值时，系统 SHALL 触发一次 OCR 文本确认。差异采样和后续 OCR SHALL 使用同一目标的 WGC 画面，不依赖桌面屏幕拷贝，也 SHALL NOT 被 WGC 到帧事件直接驱动。遮挡和失去前台焦点 SHALL NOT 暂停检测；目标隐藏、最小化、关闭或捕获不可用时 SHALL 暂停提供有效样本，失败后按当前轮询间隔有限重试，不执行无界内部重试。

#### Scenario: 无变化时不触发识别
- **WHEN** 连续两份可比较样本的像素差异低于阈值
- **THEN** 系统 SHALL 不触发 OCR，不启动新一轮识别

#### Scenario: 有变化时触发 OCR 确认
- **WHEN** 连续两份可比较样本的像素差异超过阈值
- **THEN** 系统 SHALL 从同一目标会话读取当前有效区域画面执行 OCR，并将 OCR 结果与上一次已识别的文本进行对比

#### Scenario: OCR 确认文本未变化时跳过
- **WHEN** 像素差异超阈值但 OCR 文本结果与上次完全相同
- **THEN** 系统 SHALL 不触发 LLM 分析和 overlay 更新，保持当前状态

#### Scenario: OCR 确认文本已变化时启动识别
- **WHEN** 像素差异超阈值且 OCR 文本结果与上次不同
- **THEN** 系统 SHALL 启动新一轮识别流程（全变或增量，取决于文本关系），遮挡期间只更新保存的会话，不显示浮层

#### Scenario: 目标窗口不可用时暂停检测
- **WHEN** 目标窗口被隐藏、最小化或关闭，或当前 WGC 会话不能提供有效样本
- **THEN** 系统 SHALL 不对不可用缓存执行 OCR，目标与捕获恢复可用后重新建立比较基线；关闭后的旧目标不得自动替换为其他 HWND

#### Scenario: 遮挡期间继续检测
- **WHEN** 已启用自动识别且目标被部分或完全遮挡，但仍可捕获并继续产生画面
- **THEN** 系统 SHALL 继续读取该目标区域、比较像素并按文本变化运行识别，遮挡物自身的变化不进入样本

#### Scenario: Captured pixels remain static
- **WHEN** 当前代次有效但目标没有产生变化，或反复返回相同像素
- **THEN** 系统 SHALL 按无变化处理，不强制运行 OCR 或 LLM，也不将重复像素报告为捕获失败

## ADDED Requirements

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
