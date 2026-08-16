# Add draggable recognition-region selector

## Why

当前 OCR 对整目标窗口识别,```window-word-overlay``` 会把整个窗口里的日文都划线。视觉小说/游戏窗口里常有无关 UI(按钮、菜单、立绘文字),用户想只识别一个子区域。需要一个交互式遮罩,让用户框定识别范围并持久化。

## What Changes

- **新增**一个交互式区域选择遮罩(独立于既有的点击穿透 word-overlay):选中窗口后,在窗口上叠一个遮罩,四角各有可拖拽的直角(L 形)框,中间是半透明遮罩提示覆盖范围,中间一个确定按钮。
- **拖拽**四角直角框可调整识别区域(缩放);区域限制在窗口范围内、有最小尺寸。
- **确定**后:选定区域、遮罩消失、区域持久化;识别时只处理该区域内的词。
- **主 UI** 新增一个按钮,随时重新打开遮罩重新拉区域。
- 识别流程:**捕获整窗帧 → 按区域裁剪 → 对裁剪帧 OCR → 坐标加回区域偏移**还原到整窗帧坐标,再映射屏幕。性能优先:区域外的 UI/旁白文字不再被识别,检测面积与文字行数显著减少,小区域时 OCR 明显变快。

## Capabilities

### New Capabilities
- `recognition-region`: 交互式区域选择遮罩(四角拖拽 + 半透明遮罩 + 确定按钮)、区域持久化、按区域过滤识别结果、主 UI 入口。

### Modified Capabilities
-(无现有能力的行为级变更;区域过滤发生在应用服务层,不改变 word-grouping / meikiocr-ocr 的契约)

## Impact

- **新增**:区域选择遮罩窗口类(交互式,非点击穿透,类似 `WpfOverlayRenderer` 但可捕获鼠标)、区域模型(窗口相对、归一化 0-1)、区域持久化(settings 键)、按区域过滤识别词的逻辑、主 UI 按钮 + 命令 + i18n 键。
- **修改**:`WordOverlayApplicationService`(读取区域并传给识别器)、`IWindowWordRecognizer`/`MeikiOcrWordRecognizer`(新增可选区域参数,裁剪帧 + 坐标偏移还原)、`MainWindowViewModel` + `MainWindow.xaml`(加"设置识别区域"按钮)、`ISettingsService` 新增区域配置键。
- **保留**:`IOverlayRenderer`/`WpfOverlayRenderer`(word-overlay 不变)、`meikiocr-ocr`(仍整窗识别)、`word-grouping`。