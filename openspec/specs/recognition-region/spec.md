# recognition-region Specification

## Purpose
TBD - created by archiving change add-recognition-region-selector. Update Purpose after archive.
## Requirements
### Requirement: Region selector overlay with draggable corners
系统 SHALL 在选中目标窗口并打开区域选择后,在窗口之上叠加一个交互式遮罩窗口。遮罩 SHALL 在窗口四角各显示一个可拖拽的直角(L 形)框,四框之间为半透明遮罩,提示当前框定的覆盖范围,并在区域内显示一个确定按钮。拖拽任一角 SHALL 调整该角对应的边界(缩放区域),而非移动整个区域。遮罩窗口 SHALL 能接收鼠标输入(拖拽角、点击按钮),与既有的点击穿透 word-overlay 不同。

#### Scenario: Open region selector over the window
- **WHEN** 用户选中窗口并打开区域选择
- **THEN** 窗口上出现四角直角框 + 半透明遮罩 + 确定按钮的交互式遮罩

#### Scenario: Drag a corner to resize the region
- **WHEN** 用户拖拽某个角
- **THEN** 该角对应的边界随之移动,区域随之缩放,遮罩与四框实时更新

#### Scenario: Region selector is interactive
- **WHEN** 鼠标在遮罩窗口上拖拽角或点击确定按钮
- **THEN** 操作被接收并生效(可拖拽、可点击),不被点击穿透

### Requirement: Region is clamped to the window and has a minimum size
系统 SHALL 将区域限制在目标窗口范围内,不得超出窗口边界;区域 SHALL 保持一个最小尺寸,防止拖拽到零宽/零高。拖拽角时若越界,系统 SHALL 将边界钳制在窗口内。

#### Scenario: Never exceed the window
- **WHEN** 用户把角往窗口外拖
- **THEN** 区域边界被钳制在窗口内,不超出窗口

#### Scenario: Enforce a minimum region size
- **WHEN** 用户把两个边界拖到重合
- **THEN** 区域保持最小尺寸(如不小于整窗的 1/10),不塌缩为零

### Requirement: Confirm finalizes the region and persists it
系统 SHALL 在用户点击确定按钮后:选定当前区域、关闭并隐藏遮罩、并将该区域持久化(窗口相对、归一化 0-1),供后续识别使用。再次打开区域选择时,遮罩 SHALL 以上次持久化的区域为初始值(钳制到当前窗口)。

#### Scenario: Confirm dismisses the mask and saves the region
- **WHEN** 用户拖拽好区域并点击确定
- **THEN** 遮罩关闭,区域按窗口相对坐标持久化

#### Scenario: Re-open initializes from the saved region
- **WHEN** 用户再次打开区域选择
- **THEN** 遮罩的初始四框位置等于上次保存的区域(若超出当前窗口则钳制)

### Requirement: Recognition crops to the region before OCR
系统 SHALL 在识别时裁剪到已保存的区域:捕获整窗帧后,按区域(帧坐标像素矩形)裁剪帧,对裁剪帧执行 OCR,再把识别结果的字符框坐标加回区域偏移还原到整窗帧坐标,供后续分词/下划线/查词使用。区域外的文字 SHALL 不被识别。未设置区域时 SHALL 保持整窗 OCR。

#### Scenario: OCR runs on the cropped region only
- **WHEN** 已保存区域且识别完成
- **THEN** OCR 只对区域内执行,识别出的词坐标经区域偏移还原后正确对齐到窗口,区域外文字不出现

#### Scenario: No region keeps full-window OCR
- **WHEN** 未设置区域
- **THEN** 整窗所有词照常识别与渲染

### Requirement: Main UI re-opens the region selector
系统 SHALL 在主 UI 提供"设置识别区域"入口,点击 SHALL 对当前选中窗口打开区域选择遮罩以重新拉取区域。

#### Scenario: Re-open via main UI button
- **WHEN** 用户在主 UI 点击设置识别区域按钮且已选中窗口
- **THEN** 区域选择遮罩出现在选中窗口上,可重新拖拽与确定

