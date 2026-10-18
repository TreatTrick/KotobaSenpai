## Why

`PhrasePopup` 的详情界面结构比较固定，目前却在 `Heading`、`Block` 和 `RenderWords` 中反复通过 C# 创建控件。将这部分展示迁移到 XAML，可以让间距、字体、颜色和字段排列更容易维护，同时保留现有悬停和原生窗口行为。

## What Changes

- 新增 `PhrasePopup.xaml`，声明弹窗的窗口配置、共享样式、固定内容布局、可选区块和成员词条模板。
- 引入轻量、只读的 `PhrasePopupViewModel`，直接作为 XAML 的 `DataContext`，替代命令式控件拼装；每次显示整体替换 ViewModel，覆盖短语组详情、单词释义和无释义回退三种模式，不额外增加 `PhrasePopupContent` 包装层。
- 保持现有 `PhrasePopup` 构造函数及显示方法与 `WpfOverlayRenderer` 兼容。
- 由 ViewModel 负责展示模式、字段、格式化文案和成员词列表；在视图侧保留原生定位、显示器工作区约束、物理像素与 DIP 换算、鼠标穿透、不可激活样式、辅助窗口登记、日志以及隐藏后复用行为。窗口行为默认由少量 code-behind 承担，也可委托给具体的窗口行为类。
- 保留现有内容格式、本地化查询及回退、字段顺序和默认外观。迁移时验证内容替换、尺寸限制和原生窗口行为。

## Capabilities

### New Capabilities

- `phrase-popup-presentation`：定义弹窗的声明式展示结构、只读 ViewModel 与视图的职责划分，以及现有三种显示模式的兼容要求，包括整体替换更新和受工作区约束的定位。

### Modified Capabilities

无。现有单词释义、短语组、音调和目标窗口跟踪行为保持不变；本变更新增展示层的维护约束，不修改这些能力的行为要求。

## Impact

- 受影响的实现：将 `src/KotobaSenpai.Platform.Windows/Overlay/PhrasePopup.cs` 拆为 XAML 和后台代码，并在同目录新增 `PhrasePopupViewModel.cs`。
- 受影响的验证：针对弹窗展示的检查，以及现有 `PhrasePopupPlacementTests` 和 `OverlayHoverTests`；随后进行 Windows 冒烟验证，覆盖绑定更新、内容尺寸、鼠标穿透和混合 DPI 定位。
- `KotobaSenpai.Platform.Windows` 已启用 WPF，无需新增 UI 框架、包或项目引用。Platform 继续通过 `IStringLocalizer` 获取文案，不直接引用 App 的本地化扩展或资源。
- 不修改 `WpfOverlayRenderer` 的动态画布绘制、Core 端口、OCR/LLM 处理、设置或持久化数据。不引入破坏性公共契约变更，也不承诺性能提升。
