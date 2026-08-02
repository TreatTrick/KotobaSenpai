## Why

第一阶段需要验证 VN-Learning 最核心的屏幕阅读闭环：用户选择一个日语窗口后，应用能把窗口里的文字识别为可定位的单词，并直接在原文下方提示词边界。现有仓库只有控制台文本分析 Demo，缺少 Windows 窗口选择、屏幕坐标和可视化反馈，因此现在需要建立第一个可运行的桌面垂直切片。

## What Changes

- 新增 Windows 桌面应用入口和 WPF 主窗口。
- 枚举可见顶层窗口，允许用户选择并记住一个目标窗口句柄。
- 捕获目标窗口当前画面，使用 Windows 日语 OCR 输出文本、单词和词级坐标。
- 将 OCR 词坐标转换为屏幕坐标，并以透明置顶覆盖层在每个单词下方绘制横线。
- 以 Core 领域对象隔离窗口、OCR、坐标变换和覆盖层渲染；平台服务不泄漏到领域层。
- 增加领域与平台适配测试，覆盖窗口选择、坐标边界、日语词识别和下划线布局。

## Capabilities

### New Capabilities

- `window-word-overlay`: 选择一个可见窗口，识别其日语单词的屏幕区域，并在每个单词下绘制下划线。

### Modified Capabilities

无。

## Impact

- 新增 `src/VnLearning.Core`、`src/VnLearning.Platform.Windows` 和 `src/VnLearning.App` 项目及对应测试项目。
- 依赖 Windows 10/11 的 Win32 窗口枚举、DPI 坐标和 WPF；OCR 使用系统 Windows.Media.Ocr 日语语言包。
- 保留现有 `demo` 项目不变；第一阶段不引入网络、LLM、持久化截图或账号系统。
