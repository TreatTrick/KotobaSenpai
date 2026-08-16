# Tasks: Add draggable recognition-region selector

## 1. 区域模型与持久化

- [x] 1.1 新增 `src/KotobaSenpai.Core/Models/RecognitionRegion.cs`：窗口相对、归一化 0-1 的 `{X, Y, Width, Height}` 值对象，校验都在 [0,1]、宽高 > 0。
- [x] 1.2 新增 settings 键（如 `RecognitionRegion` = `x,y,w,h` 字符串），`ISettingsService` 读写；缺省表示"全窗"。
- [x] 1.3 提供 `RecognitionRegion` ↔ 窗口像素矩形换算（`ToWindowPixels(windowBounds)` / `FromWindowPixels`），含钳制到窗口 + 最小尺寸。
- [x] 1.4 为换算/钳制/最小尺寸写单测。

## 2. 区域选择遮罩（交互式）

- [x] 2.1 新增 `RegionSelectorWindow`（WPF，仿 `WpfOverlayRenderer.OverlayWindow` 的透明置顶无激活，但**不禁用命中测试**），对齐到目标窗口边界。
- [x] 2.2 四角绘制可拖拽的直角框 + 半透明遮罩（区域外或区域内遮罩提示）+ 中间确定按钮。
- [x] 2.3 鼠标事件：命中角 → 拖拽更新区域并钳制到窗口 + 最小尺寸；四框/遮罩实时更新。
- [x] 2.4 确定按钮 → 把当前区域按窗口归一化持久化到 settings，关闭遮罩。
- [x] 2.5 打开遮罩时以已保存区域为初始四框位置（钳制到当前窗口）。

## 3. 识别裁剪到区域再 OCR（性能优先）

- [x] 3.1 `IWindowWordRecognizer.RecognizeAsync` 新增可选区域参数（帧坐标像素矩形）；`MeikiOcrWordRecognizer` 捕获整窗帧后按区域裁剪帧 → 对裁剪帧 OCR → 结果字符框坐标加回区域偏移还原到整窗帧坐标。
- [x] 3.2 `WordOverlayApplicationService` 从 settings 读取区域（归一化）换算成帧像素矩形传给识别器；未设置区域时传 null 保持整窗。
- [x] 3.3 为裁剪 + 偏移还原写单测（区域内词坐标正确还原、区域外不识别、无区域整窗、极小区域回退整窗）。

## 4. 主 UI 入口

- [x] 4.1 `MainWindowViewModel` 加"设置识别区域"命令：对选中的窗口打开区域选择遮罩。
- [x] 4.2 `MainWindow.xaml` 加按钮；新增 i18n 键（如 `Button_SetRecognitionRegion`/`Region_Confirm`/`Region_DragHint`）。
- [x] 4.3 未选中窗口时按钮给出提示（复用 `Status_SelectTargetFirst`）。

## 5. 收尾

- [x] 5.1 全量 `dotnet build` + `dotnet test` 通过。
- [x] 5.2 更新 `docs/` 相关描述（识别区域功能）。
- [x] 5.3 `openspec validate` + `openspec archive` 落定 delta 到主 specs。