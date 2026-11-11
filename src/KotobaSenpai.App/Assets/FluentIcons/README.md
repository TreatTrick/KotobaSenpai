# Fluent color icons

These assets come from [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons), licensed under MIT.

Pinned upstream commit: `463d58b8186f874ab2f51cf0a08b4ff5097c912f`.

| Application use | Upstream asset |
| --- | --- |
| ScanColorIcon | [scan_type](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/463d58b8186f874ab2f51cf0a08b4ff5097c912f/assets/Scan%20Type/SVG/ic_fluent_scan_type_24_color.svg) |
| SettingsColorIcon | [settings](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/463d58b8186f874ab2f51cf0a08b4ff5097c912f/assets/Settings/SVG/ic_fluent_settings_24_color.svg) |
| BookOpenColorIcon | [book_open](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/463d58b8186f874ab2f51cf0a08b4ff5097c912f/assets/Book%20Open/SVG/ic_fluent_book_open_24_color.svg) |
| InfoColorIcon | [info (filled)](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/463d58b8186f874ab2f51cf0a08b4ff5097c912f/assets/Info/SVG/ic_fluent_info_24_filled.svg) |

The original SVG files are retained here. `Themes/FluentColorIcons.xaml` translates their paths and absolute linear gradients to WPF DrawingImage resources. The transparent 24 x 24 drawing preserves the original viewport, and the resources remain vector-based at every DPI. The settings gear adds a blue center to match the Windows Settings visual style. The information icon uses the upstream filled geometry with a blue gradient and white information glyph matching the scan icon's palette.

The license is copied alongside the application when building and publishing.
