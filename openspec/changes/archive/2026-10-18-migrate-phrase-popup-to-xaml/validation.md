# 验证记录

日期：2026-10-18。

## 实现范围

- `PhrasePopup.xaml` 定义窗口外观、共享样式、可选区块和成员词模板。
- 只读 `PhrasePopupViewModel` 直接作为 `DataContext`，每次显示整体替换。快照属性使用 `OneTime` 绑定，模板内部对窗口区块 `Tag` 的绑定保留常规更新。
- `PhrasePopup.xaml.cs` 保留原生定位、物理像素与 DIP 处理、日志、鼠标穿透和窗口生命周期。与变更前逐段比较，`Position`、`CalculatePlacement`、原生样式和函数导入保持一致。
- 未修改 `WpfOverlayRenderer`、Core、App、项目引用或包依赖，也无需更改测试项目配置。

## 自动化检查

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/KotobaSenpai.Platform.Windows.Tests/KotobaSenpai.Platform.Windows.Tests.csproj --verbosity minimal` | 158 通过，0 失败，0 跳过 |
| `dotnet test tests/KotobaSenpai.App.Tests/KotobaSenpai.App.Tests.csproj --verbosity minimal` | 92 通过，0 失败，0 跳过 |
| `dotnet build KotobaSenpai.slnx --verbosity minimal` | 构建成功，0 警告，0 错误 |
| `openspec validate migrate-phrase-popup-to-xaml --strict` | 校验通过 |

新增 5 项 ViewModel 测试和 4 项真实 WPF STA 测试用例。ViewModel 测试覆盖成员顺序、与可变输入隔离、只读成员集合、平假名与有序音调格式、空可选字段、三种模式、本地化查询与英文回退，以及新旧实例互不影响。现有弹窗定位和覆盖层悬停测试一并通过。

## Windows 冒烟验证

STA 测试实际创建、显示和关闭 WPF 窗口，不依赖 App 资源：

- 短语组、单词释义、无释义回退和空短语连续切换后，已生成的视觉树展示当前内容，可选区块正确折叠。
- 分别使用英文回退和测试本地化器，检查实际绑定文本及模式切换后的区块标题。
- 短内容、包含多个成员词的长内容、短内容依次更新，初次与恢复后的短内容尺寸一致；长内容宽高受 360 DIP 上限约束，短内容遵守 200 DIP 最小宽度策略。
- 原生扩展样式包含鼠标穿透、不可激活和工具窗口标志。显示前后的线程活动窗口保持一致；`WindowFromPoint` 在弹窗覆盖的位置命中下方测试窗口。
- 隐藏后保留注册与 HWND，重新显示复用原窗口；关闭后注册表中不再保留该 HWND。
- 靠近显示器右下角时，实际窗口移动到锚点上方并位于显示器工作区内。

本次原生检查记录为 `DPI=120`，即 125% 缩放，工作区为 `(0,0)-(3440,1440)`。100%/150%/200% 缩放下的真实窗口以及跨不同 DPI 显示器移动未实测；现有定位单元测试覆盖不同像素尺寸和负坐标显示器。未修改系统缩放设置，也未执行真实鼠标点击；鼠标穿透由原生样式和窗口命中测试验证。

## 测试发现及修复

1. 最初的展示绑定测试因 `DataContext` 为空而失败，证明能检测迁移前的命令式展示方式。
2. 初次绑定时，单次 `Measure` 在模板内延迟绑定完成前读取了尺寸。“短、长、短”用例复现了初次和恢复后高度不同。现通过 `Measure`、`UpdateLayout`、再次 `Measure` 完成布局后读取尺寸，避免额外的调度器消息循环。
3. 原有关闭回调重新读取 HWND 时，WPF 已将 `WindowInteropHelper.Handle` 清零，导致注销无效。现于 `SourceInitialized` 捕获登记的句柄，并在 `Closed` 使用同一值注销；真实窗口用例验证登记、隐藏复用和注销。

## 审查

按 `requesting-code-review` 完成独立代码审查，未发现需要修复的问题。审查确认只读快照适合显式 `OneTime` 绑定，且区块模板的 `Tag` 绑定应继续跟随视图属性更新；调整后完整 Platform 和 App 测试及解决方案构建再次通过。

`OneTime` 的选择参考 [WPF BindingMode 官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.windows.data.bindingmode?view=windowsdesktop-10.0)，用于匹配按次替换的只读展示方式，并不宣称修复了已证实的内存泄漏或提升了经过测量的性能。
