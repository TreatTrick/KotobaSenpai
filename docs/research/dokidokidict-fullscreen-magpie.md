# DokiDokiDict 全屏 Overlay 问题与 Magpie 调研

**调研日期：** 2026-07-31
**调研方法：** 基于 DokiDokiDict 源码（`D:\project\DokiDokiDict`，`github.com/elwendys/DokiDokiDict`，v0.9.5）的实证分析，结合 Windows 图形栈（DWM / 独占全屏）的通用机制。本文回答两个问题：① 为什么游戏全屏后假名 overlay 与快捷键失效；② 我们的 .NET 版能否解决，以及 Magpie 在其中的角色。
**相关文档：** [DokiDokiDict 的 C# 复刻可行性调研](./dokidokidict-csharp-feasibility.md)、[竞品快照](./dokidokidict-competitive-snapshot.md)、[VN-Learning 项目计划](../../VN-Learning-Project.md)

## 结论（TL;DR）

全屏下 overlay 不可见，是 **Windows 的 DWM（桌面窗口管理器）机制限制，不是 Python/C# 的语言差异**。游戏以"独占全屏"运行时会绕过 DWM 直接输出到屏幕，而所有"透明置顶 overlay"（Qt 的 `WA_TranslucentBackground`、WPF 的 `AllowsTransparency+Topmost`、WinUI 的 overlay）都依赖 DWM 合成到屏幕--DWM 被绕过，overlay 就画不上去。

DokiDokiDict **并未真正解决**这个问题，而是**绕开**：要求游戏用无边框窗口化，或用 Magpie 把游戏转成 DWM 合成的无边框全屏，overlay 再画在 Magpie 输出窗口之上。源码确认：其 Magpie 集成（`magpie_manager.py`）只做坐标变换，并非"通过 Magpie 渲染 overlay"。

**我们的 .NET 版能做到和 DokiDokiDict 一样**（无边框 + Magpie 下 overlay 正常），且 Magpie 适配在 C# 里更顺手（纯 Win32 P/Invoke）。快捷键可用低级键盘钩子独立实现，可能比 DokiDokiDict 更稳。唯一"更强"的方案是 DLL 注入 + D3D hook，属于重活，MVP 不必碰。此外 Windows 11 + DX12 游戏大多已不走真独占全屏，实际影响比想象中小。

## 问题本质：为什么全屏下 overlay 失效

Windows 下的"全屏"分两种，问题只出在后者：

| 显示模式 | 是否经 DWM 合成 | 透明置顶 overlay 能否显示 |
| --- | --- | --- |
| 窗口化 / 无边框窗口化（borderless） | 是（普通窗口） | ✅ 能 |
| 独占全屏（exclusive fullscreen） | 否（游戏直接接管显卡输出，绕过 DWM） | ❌ 不能 |

- **DWM（Desktop Window Manager）** 是 Windows 的桌面合成器，所有普通窗口（包括分层透明窗口）都由它合成到屏幕上。
- 独占全屏时，游戏用 DXGI flip / 全屏独占模式直接 swap 到显示输出，**DWM 不参与**，因此任何靠 DWM 合成的 overlay 都不可见。
- 这与实现语言无关：Qt 的 `WA_TranslucentBackground`、WPF 的 `AllowsTransparency`、WinUI 的透明 overlay，底层都是 DWM 合成的分层窗口，**一视同仁**。

快捷键则是另一回事：它属于输入层而非图形层。`RegisterHotKey` 或 `SetWindowsHookEx(WH_KEYBOARD_LL)` 在 OS 输入层工作，**独占全屏游戏一般也能拦到**。若 DokiDokiDict 称全屏下快捷键也失效，多半因其弹窗流程整体依赖 overlay 窗口能显示/交互，而非按键本身没收到。

## DokiDokiDict 的实际做法（源码实证）

### 1. Overlay 是标准 Qt 透明置顶工具窗口

`src/gui/furigana_overlay.py` 中 `FuriganaOverlay(QWidget)` 的窗口属性（约 79–86 行）：

```python
self.setWindowFlags(
    Qt.WindowType.FramelessWindowHint |
    Qt.WindowType.WindowStaysOnTopHint |
    Qt.WindowType.Tool |
    ...
)
self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)
```

这是典型的 DWM 合成分层 overlay。在独占全屏下同样会失效--DokiDokiDict 没有魔法。

### 2. 源码中没有处理独占全屏的逻辑

对 `src/` 全量搜索 `fullscreen / exclusive / borderless` 等关键词，命中均为无关项：阅读器自身 UI 全屏（`reader_window.py` 的 F11）、SRS 图片全屏查看、`window_picker` 的全屏选择遮罩、注释里的 "mutually exclusive" / "end_index_exclusive" 等。**没有任何把独占全屏转为无边框、或检测独占全屏的代码**。结论：DokiDokiDict 不尝试在独占全屏下显示 overlay，而是依赖外部条件（游戏无边框 / Magpie）让 DWM 保持激活。

### 3. Magpie 集成只做坐标变换

`src/gui/magpie_manager.py`（`MagpieTransformer` 单例）的文件 docstring 即点明：

> "Magpie is a Windows tool that scales game windows to fill the screen. When Magpie is active, mouse coordinates need to be transformed from the scaled visual position to the original game coordinates."

其实现（关键点）：

- 通过 `FindWindowW` 查找 Magpie 输出窗口，类名 `Window_Magpie_967EB565-6F73-4E94-AE53-00CC42592A22`（第 19 行）。
- 通过 `GetPropW` 读取该窗口的属性 `Magpie.SrcLeft/Top/Right/Bottom`（原始游戏矩形）与 `Magpie.DestLeft/Top/Right/Bottom`（缩放后屏幕矩形）。
- `transform_raw_to_visual()` 把鼠标原始坐标按 src->dest 映射成视觉坐标，供 OCR 区域选择/取词定位使用。
- 受 `config.magpie_compatibility` 开关控制；注释提到 `GetCursorPos()` 在弹窗干扰 Magpie 的 `ClipCursor` 时会返回异常坐标，故对 src 矩形做 clamp。

**也就是说：Magpie 的真正作用是"让游戏不处于独占全屏"**（输出为 DWM 合成的无边框全屏窗口），overlay 仍是 DokiDokiDict 自己画的普通窗口。Magpie 集成本身不负责渲染，只负责坐标对齐。

## Magpie 是什么

Magpie 是**免费开源的 Windows 窗口缩放工具**（GitHub: [Blinue/Magpie](https://github.com/Blinue/Magpie)，GPL），可视为 Steam 收费工具 *Lossless Scaling* 的免费开源平替。

- **功能**：把任意窗口（主要游戏/VN）用高质量算法放大到全屏。游戏以窗口模式运行，按快捷键（默认 `Alt+F11`）即捕获该窗口、缩放、铺满屏幕，输出为 Magpie 自己的无边框全屏窗口。
- **算法**：Anime4K（动漫/VN 放大主力，平涂线稿效果好）、FSR/FSR2、FSRCNNX、Lanczos、NNEDI3、RAVU、ACNet 等；新版本还支持插帧（frame generation）。
- **关键属性**：输出窗口是 DWM 合成的普通无边框窗口，**不是独占全屏**--这正是 overlay 能显示的原因。
- **为何 VN/日语学习者爱用**：① 老 VN 原生分辨率低，Anime4K 放大到 4K 锐利好；② 把独占全屏转无边框，查词/振假名 overlay 才显示得出来；③ 免费。
- **对我们**：Magpie 是独立软件，用户自装。.NET 版不需"实现"它，只需像 DokiDokiDict 那样**适配**它（检测输出窗口 + 读 Src/Dest 做坐标变换 + overlay 画在上面）。

## .NET 版能否解决？按"解决"的三种含义

| 含义 | 能否 | 说明 |
| --- | --- | --- |
| A. 做到和 DokiDokiDict 一样（无边框/Magpie 下 overlay 正常） | ✅ 能，更顺手 | WPF/WinUI3/Avalonia 透明置顶 overlay 在任何 DWM 合成窗口上都正常显示，与 Qt 等价。Magpie 适配在 C# 里更自然：`magpie_manager.py` 全是 Win32 P/Invoke（`FindWindowW`/`IsWindowVisible`/`GetPropW`），约 200 行，用 `[DllImport("user32")]` 照搬即可，甚至更短。 |
| B. 不依赖 Magpie/无边框，真把 overlay 画到独占全屏游戏画面上 | ⚠️ 技术上能，很重，不建议 | 唯一办法是 DLL 注入 + 渲染 API hook（Discord/Steam/RTSS 做法）：注入游戏进程，hook DXGI `Present()`/D3D9 `EndScene`/Vulkan/OpenGL，在游戏自己的帧里画字。C# 可用 EasyHook 等，但渲染 hook 的注入 DLL 通常需 C++ 原生，工作量大、脆、每个 API/架构要对齐。**DokiDokiDict 自己都没做**，MVP 不建议碰。 |
| C. 全屏下快捷键 | ✅ 基本能，且独立于 overlay | 用 `SetWindowsHookEx(WH_KEYBOARD_LL)` 低级键盘钩子在 OS 输入层拦截，独占全屏游戏一般也拦得住，比 `RegisterHotKey` 更稳。可与 overlay 渲染解耦，单独做扎实。 |

## 一个对 .NET 版有利的好消息

**Windows 11 + DX12 游戏大多已不走真独占全屏。** Windows 10 1903 起的"全屏优化（Fullscreen Optimizations, FSO）"会自动把大多数游戏的独占全屏转成无边框（DWM 保持激活），overlay 本来就能用。真正坚持独占全屏的，主要是老 DX9–11 游戏、或显式关闭 FSO 的游戏。即现代 Win11 上该问题的实际影响比听起来小，.NET overlay 在多数情况直接可用。

## 实操建议（给 .NET 版）

1. **默认要求无边框窗口化**：游戏内设"无边框/窗口化全屏"，并保证 Windows 的"全屏优化"未被关闭。文档写清楚。
2. **移植 Magpie 集成**：照 `src/gui/magpie_manager.py` 把 `FindWindowW` + `GetPropW` 读 `Magpie.Src*/Dest*` 的坐标变换翻成 C# `MagpieTransformer`（~200 行 P/Invoke）。可选自动检测/拉起 Magpie。
3. **快捷键用 `WH_KEYBOARD_LL`** 低级钩子，独立于 overlay 渲染。
4. **文档声明**：不支持真独占全屏（与 DokiDokiDict 一致），改用无边框或 Magpie。
5. **长期可选项**：若确有真独占全屏 overlay 的硬需求，再评估 D3D/DXGI hook 注入方案（单列里程碑，非 MVP）。

## 待验证事项

- 在目标 VN/游戏上实测：无边框窗口化下 WPF/WinUI overlay 是否稳定置顶、不抢焦点、点击穿透正确。
- Magpie 适配移植后，`Magpie.Src*/Dest*` 坐标变换在不同 Magpie 版本/多显示器/负坐标（主显示器左侧）下是否与 Python 版一致（注意 `magpie_manager.py` 中对 `LONG` 有符号 32 位重解释的处理）。
- `WH_KEYBOARD_LL` 在目标独占全屏游戏下的实际拦截成功率（少数游戏用 raw input 独占前台可能干扰）。

## 来源登记

1. 源码：`D:\project\DokiDokiDict`（`github.com/elwendys/DokiDokiDict`，v0.9.5）。本文关于 overlay 窗口属性、Magpie 集成实现、无独占全屏处理逻辑的结论，分别来自 `src/gui/furigana_overlay.py`、`src/gui/magpie_manager.py` 及对 `src/` 的全量关键词搜索。
2. Windows 图形栈机制：DWM 合成、独占全屏绕过 DWM、Fullscreen Optimizations（Win10 1903+）等属 Windows 通用机制，基于公开的 Windows 图形文档常识。
3. Magpie：开源项目 [Blinue/Magpie](https://github.com/Blinue/Magpie)，GPL 协议；功能与算法描述基于该项目公开资料。
4. 本地项目上下文：[VN-Learning 项目计划](../../VN-Learning-Project.md)、[C# 复刻可行性调研](./dokidokidict-csharp-feasibility.md)。
