## 1. 设置键与渲染器改动

- [x] 1.1 在 `KotobaSenpai.Platform.Windows/Overlay` 定义共享设置常量类（如 `FuriganaSettings`）：键 `FuriganaFontScale`、默认比例 `1/3.0`。
- [x] 1.2 在 `WpfOverlayRenderer.cs` 加判定函数 `ContainsKanji(string)`：表面文本是否含 CJK 表意字符（`\p{IsCJKUnifiedIdeographs}`）。
- [x] 1.3 给 `WpfOverlayRenderer` 构造加可选 `ISettingsService? settings = null`（DI 已注册单例，自动注入）；在 `Render` 中解析 `FuriganaFontScale`（`double.TryParse`，缺失/非法回退 `1/3.0`）。
- [x] 1.4 在 `OverlayWindow.Render` 中，为每个含汉字的词（按 `session.Words` 的 union `Bounds`）与下划线同批次增加 `TextBlock`：文本为 `Kana.ToHiragana(word.Reading)`，`FontSize = Bounds.Height / scale × scaleFactor`，水平居中于 `Bounds`，置于 `Bounds.Y` 上方（留 ~2 DIP 间隙）。
- [x] 1.5 顶部裁剪：若振假名顶部要超出覆盖层窗口，夹紧到窗口内，避免绘制到屏幕外。
- [x] 1.6 确认刷新/隐藏时振假名随 `_canvas.Children.Clear()` 一并清除，无残留；悬停逻辑不触碰振假名，点击穿透不变。

## 2. 设置面板 UI

- [x] 2.1 在 `MainWindow.xaml`"外观"卡片加一个 Slider（范围 0.1–0.5，默认 1/3）与标签，绑定一个新的依赖属性 `SettingsService: ISettingsService`。
- [x] 2.2 在 `MainWindow.xaml.cs` 加 code-behind：窗口初始化时读 `FuriganaFontScale`（缺省 1/3）设置滑块值，滑块 `ValueChanged` 时写回 settings（用 `_syncing` 防重入，与主题同模式）。
- [x] 2.3 在 `App.xaml.cs` 构造 `MainWindow` 时注入 `ISettingsService` 到新依赖属性。

## 3. 测试

- [x] 3.1 在 `KotobaSenpai.Platform.Windows.Tests` 加测试：`ContainsKanji` 对含汉字返回 true、对纯平假名/片假名/英文返回 false。
- [x] 3.2 加测试：`FuriganaFontScale` 解析逻辑——缺失、非法、合法值分别回退默认/取该值。
- [x] 3.3 构建并运行测试，确认通过。

## 4. 验证

- [x] 4.1 `dotnet build` 全解决方案无错误。
- [x] 4.2 运行应用，对一段含汉字日文做识别，肉眼确认振假名以默认 1/3 字号居中显示在汉字词上方、纯假名词不显示；在设置面板调滑块后重新识别，字号随之变化。
