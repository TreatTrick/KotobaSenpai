# recognition-region Specification

## MODIFIED Requirements

### Requirement: Region selector overlay with draggable corners

系统 SHALL 在选中目标窗口并打开区域选择后,在目标客户区上方叠加一个交互式遮罩窗口。遮罩 SHALL 在窗口四角各显示一个可拖拽的直角(L 形)框,四框之间为半透明遮罩,提示当前框定的覆盖范围,并在区域内显示一个确定按钮。拖拽任一角 SHALL 调整该角对应的边界(缩放区域),而非移动整个区域。遮罩窗口 SHALL 能接收鼠标输入(拖拽角、点击按钮),与既有的点击穿透 word-overlay 不同。遮罩 SHALL 位于目标窗口上方并跟随目标窗口的 Z 顺序，不得在其他窗口遮挡目标时浮在遮挡窗口之上。目标窗口移动、缩放或 DPI 变化时，遮罩 SHALL 按保存的归一化区域和最新客户区快照实时重定位和重绘。

#### Scenario: Open region selector over the window

- **WHEN** 用户选中窗口并打开区域选择
- **THEN** 窗口上出现四角直角框 + 半透明遮罩 + 确定按钮,并与当前目标客户区对齐

#### Scenario: Drag a corner to resize the region

- **WHEN** 用户拖拽某个角
- **THEN** 该角对应的边界随之移动,区域随之缩放,遮罩与四框实时更新

#### Scenario: Region selector is interactive

- **WHEN** 鼠标在遮罩窗口上拖拽角或点击确定按钮
- **THEN** 操作被接收并生效(可拖拽、可点击),不被点击穿透

#### Scenario: Follow target movement while selecting

- **WHEN** 区域选择器打开期间目标窗口移动、缩放或 DPI 发生变化
- **THEN** 遮罩窗口跟随最新客户区移动和缩放,当前归一化区域仍覆盖目标窗口中的相同比例位置

#### Scenario: Hide when target is occluded or minimized

- **WHEN** 其他窗口遮挡目标或目标窗口被最小化
- **THEN** 区域选择器按目标 Z 顺序被遮挡或隐藏,不接收来自其他窗口的交互

### Requirement: Region is clamped to the window and has a minimum size

系统 SHALL 将区域限制在目标窗口范围内,不得超出窗口边界;区域 SHALL 保持一个最小尺寸,防止拖拽到零宽/零高。拖拽角时若越界,系统 SHALL 将边界钳制在窗口内。窗口改变大小时，系统 SHALL 先从归一化区域重新计算当前像素边界，再应用最小尺寸约束。

#### Scenario: Never exceed the window

- **WHEN** 用户把角往窗口外拖
- **THEN** 区域边界被钳制在窗口内,不超出窗口

#### Scenario: Enforce a minimum region size

- **WHEN** 用户把两个边界拖到重合
- **THEN** 区域保持最小尺寸(如不小于整窗的 1/10),不塌缩为零

#### Scenario: Recalculate after resize

- **WHEN** 目标窗口大小改变且区域选择器仍打开
- **THEN** 当前区域按归一化坐标映射到新窗口,并继续满足窗口边界和最小尺寸约束

### Requirement: Confirm finalizes the region and persists it

系统 SHALL 在用户点击确定按钮后:选定当前区域、关闭并隐藏遮罩、并将该区域持久化(窗口相对、归一化 0-1),供后续识别使用。再次打开区域选择时,遮罩 SHALL 以上次持久化的区域为初始值(钳制到当前窗口)。目标窗口移动或缩放不得改变已保存区域的归一化值。

#### Scenario: Confirm dismisses the mask and saves the region

- **WHEN** 用户拖拽好区域并点击确定
- **THEN** 遮罩关闭,区域按窗口相对坐标持久化

#### Scenario: Re-open initializes from the saved region

- **WHEN** 用户再次打开区域选择
- **THEN** 遮罩的初始四框位置等于上次保存的区域(若超出当前窗口则钳制)

#### Scenario: Preserve normalized region through target resize

- **WHEN** 用户保存区域后目标窗口改变位置或大小
- **THEN** 设置中的归一化区域不被改写,下一次选择或 OCR 使用新窗口尺寸重新计算像素区域
