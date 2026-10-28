## MODIFIED Requirements

### Requirement: Window selection

系统 SHALL 通过显式桌面拾取允许用户选择一个具有有效 HWND 和客户区的可见顶层窗口，并遵循 `visual-window-picker` 的候选、输入、取消和预览规则。系统 SHALL 移除目标窗口下拉列表、刷新入口及启动时自动选择首个窗口的行为。选择结果和用于确认选择的静态缩略图 SHALL 只保存在当前进程内，不得将窗口截图或原始内容持久化或上传。选择只绑定目标和获取预览，SHALL NOT 自动启动 OCR、LLM 或自动识别。

#### Scenario: Select a visible window
- **WHEN** 用户点击选择按钮、移动到一个有效可见顶层窗口并单击确认
- **THEN** 系统返回该窗口的句柄、标题和客户区屏幕边界，将其设为当前目标，并按视觉拾取规格获取预览

#### Scenario: Ignore unusable windows
- **WHEN** 拾取候选不可见、已销毁、已最小化或没有有效客户区
- **THEN** 系统不允许确认该候选，并继续允许选择其他有效窗口或取消

#### Scenario: Start without an implicit target
- **WHEN** 主窗口初始化且用户尚未显式选择窗口
- **THEN** 当前目标为空，界面显示选择提示，不枚举并自动选中首个窗口

#### Scenario: Cancel without replacing the selection
- **WHEN** 用户进入拾取后按 Esc，且原目标仍然有效
- **THEN** 系统恢复主窗口并保留原目标及原静态缩略图，不提交悬停候选

#### Scenario: Recognize only after an explicit recognition request
- **WHEN** 用户完成窗口选择或预览重试，但未请求开始识别
- **THEN** 系统保持待识别状态和彩虹边框，不调用 OCR 或 LLM，也不因自动识别设置已开启而启动新的识别会话；用户请求开始识别时先移除彩虹边框
