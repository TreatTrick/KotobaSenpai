# 验证记录

## 自动检查

执行：

```powershell
dotnet test KotobaSenpai.slnx --artifacts-path .codex/tmp/overlay-appearance -v quiet -m:1 -- xUnit.ParallelizeTestCollections=false
dotnet build src/KotobaSenpai.App -c Release --artifacts-path .codex/tmp/overlay-appearance-release -v quiet -m:1
openspec validate customize-overlay-colors-and-outlines --strict
git -c core.safecrlf=false diff --check
```

- 完整测试：691 项通过，0 项失败，0 项跳过。App 214、Architecture 34、Core 166、Platform.Windows 277。
- Release 构建：0 警告、0 错误。
- 严格规范校验和空白检查通过。
- 独立只读代码审查完成，没有发现需要修复的问题。
- 使用独立输出目录，没有改动用户的真实设置、API 密钥或桌面缩放。

## 覆盖范围

- 颜色合法性、大小写统一、旧配置缺字段和坏值回退。
- 黑白描边选择，关闭时不保留旧阴影。
- 描边自动、黑色、白色模式切换，0.5～3 DIP 粗细渲染、统一小数格式存储和非法配置回退。
- 关闭描边时颜色与粗细控件置灰，重新开启保留选择；重置填充颜色保留全部描边偏好。保存失败不更新外观或发送刷新通知。
- 普通读音、H/L 文字和圆点、下划线各自使用正确颜色。
- 100%、125%、150%、200% 的离屏 DPI 渲染，检查小字号默认和最大粗细的描边；真实窗口检查边缘留白、原文坐标对齐和既有点击穿透。
- 外观刷新保留相同的绘制元素、坐标、悬停状态和释义弹窗；隐藏后更新也能生效。刷新入口只替换画刷与描边，不调用识别服务。
- 颜色选择器确认、取消、非法输入、Enter 确认和 Esc 取消。
- Photoshop 式二维色盘与竖直色相条，HSV/RGB 转换、黑灰色相保留、越界选择限制、方向键微调，以及色盘与 HEX 双向同步。
- 检查色盘实际渲染出的颜色和选择结果一致；输入错误 HEX 后，再次选择相同 RGB 颜色也能恢复正确草稿。
- 使用临时 JSON 文件验证重新读取设置、重置颜色保留描边状态，以及文件被锁定时重置失败且旧值保持不变。
- 中英文界面、深浅主题、颜色控件的可访问名称和卡片内部提示。

四种界面截图已生成并检查，保存在忽略目录：

```text
.codex/tmp/overlay-appearance/bin/KotobaSenpai.App.Tests/debug/previews/overlay-appearance-{en|zh-CN}-{Light|Dark}.png
.codex/tmp/overlay-appearance/bin/KotobaSenpai.App.Tests/debug/previews/color-picker-{en|zh-CN}-{Light|Dark}.png
```

DPI 检查使用自动渲染，没有逐次切换真实显示器的系统缩放。跨显示器拖动仍可在实际使用时补充体验检查。
