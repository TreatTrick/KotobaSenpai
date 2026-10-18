## MODIFIED Requirements

### Requirement: Recognition crops to the region before OCR
系统 SHALL 在识别时从选中 HWND 的 WGC 画面提取客户区及已保存的客户区相对区域，对该区域执行 OCR，再将识别字符框坐标加回区域偏移还原到完整客户区帧坐标，供后续分词、下划线和查词使用。区域外文字、标题栏、边框及其他顶层窗口 SHALL NOT 被识别。系统 SHALL 保留现有归一化区域设置、区域解析与钳制规则；未设置有效区域时 SHALL 保持完整客户区 OCR。区域选择 UI 本身仍遵守遮挡隐藏规则。

#### Scenario: OCR runs on the cropped region only
- **WHEN** 已保存区域且识别完成
- **THEN** OCR 只对 WGC 帧中对应的客户区选区执行，识别出的词坐标经区域偏移还原后正确对齐到窗口，区域外文字不出现

#### Scenario: No region keeps full-window OCR
- **WHEN** 未设置区域
- **THEN** 完整客户区内的所有词照常识别与渲染，标题栏和边框不进入 OCR 输入

#### Scenario: Read a covered region
- **WHEN** 保存的识别区域被另一窗口覆盖且目标仍可由 WGC 捕获
- **THEN** 系统识别该目标区域自身的像素，不读取遮挡窗口或桌面矩形

#### Scenario: Preserve region mapping across window styles and DPI
- **WHEN** 目标在有标题栏、最大化、无边框模式间切换，或移动到不同 DPI 的显示器
- **THEN** 系统从最新客户区换算已保存归一化区域，重新校准帧偏移，OCR 结果仍以完整客户区为坐标基准
