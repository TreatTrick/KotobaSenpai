用户已接管实际窗口验证。下列实现任务的勾选表示代码、对应自动化测试及代码审查完成；真实 HWND、驱动行为与资源增长不因此视为通过，统一保留在 1.3、4.4、6.2 和 [验证记录](validation.md) 中。规范同步/归档在验收后进行。

## 1. Validate the native capture assumptions

- [x] 1.1 在现有 `net10.0-windows10.0.19041.0` 下验证 SDK WinRT 投影、`Vortice.Direct3D11` 和 HWND/DXGI 桥接可构建运行，固定经过验证的包版本并记录必要依赖与许可证；不引入新的 UI/视频框架。
- [x] 1.2 用最小验证窗口持续显示变化的英文帧编号，验证 WGC 在部分/完全遮挡和失去焦点时只返回目标内容；再验证实际使用的游戏，分别记录隔离效果与后台是否继续渲染，不将后者当作 API 保证。
- [ ] 1.3 用户验收：校准有边框、最大化、无边框窗口的原始帧与客户区原点，覆盖 100%/125%/150% DPI 和负桌面坐标；优先复查最大化偏移，记录于本变更目录，未确认前不宣称实机通过。

## 2. Define the capture and visibility contracts

- [x] 2.1 更新 `TargetWindowSnapshot`，分离 `IsCapturable` 与 `IsRenderable`；补充有效/隐藏/最小化/失焦/部分及完全遮挡的状态测试，保留 tracker 的事件和 Z 顺序规则。
- [x] 2.2 为 `IWindowFrameCapture` 增加幂等 `Stop()`，为 `CapturedFrame` 增加兼容现有构造调用的 `CaptureGeneration` 元数据，更新端口/区域注释及受影响 fake；保持 BGRA 数据和客户区坐标语义。
- [x] 2.3 增加捕获不支持、失败和超时的稳定错误码及所有现有 culture 的 `.resx` 文案，修改 `TargetWindowUnavailable` 提示以移除“必须无遮挡”的条件；验证错误码解析与调用方取消行为，代码、注释和测试名称/诊断使用英文。
- [x] 2.4 按用户最新决策将隐藏阈值改为完整客户区遮挡并集严格超过 30%；覆盖恰好 30%、多个窗口累计、重叠去重、客户区裁剪及大面积计算，删除临时任务栏细边缘特判。

## 3. Implement the WGC frame adapter

- [x] 3.1 实现 `WgcWindowFrameCapture` 的支持检查、`CreateForWindow`、D3D11 device/WinRT 桥接、free-threaded frame pool 和 session 初始化；禁用光标捕获，使用正常系统捕获边框，不激活或恢复目标。
- [x] 3.2 实现客户区及区域纹理裁剪、staging texture 读回和 `RowPitch` 逐行复制；以带行 padding、非零客户区/区域偏移和越界内容的样本验证字节、尺寸、坐标与返回内存所有权。
- [x] 3.3 实现只保留最新拥有纹理的接收路径，串行化 D3D11 操作；实现新会话首个有效帧的 2 秒等待、取消、静态帧复用以及到帧快于采样时不累积队列、不执行 OCR，真实行为交由 6.2 验收。
- [x] 3.4 实现 `ContentSize`/客户区尺寸校验、pool/texture 重建和代次更新；自动测试拒绝不匹配的尺寸/代次，原生 resize 及设备/item 失败重试交由 6.2 验收，内部不进行无界重试。
- [x] 3.5 实现 `Stop()`、`IDisposable` 与 tracker 事件衔接，处理最小化/隐藏/销毁、目标切换、HWND 复用及晚到回调；自动测试取消、重复停止/释放和旧识别请求失效，原生回调与资源释放交由 6.2 验收。

## 4. Integrate manual recognition and UI lifecycle

- [x] 4.1 将 App DI 切换为 WGC，移除 GDI 实现与运行时注册，更新识别器和捕获接口中的屏幕拷贝说明；确认生产识别路径没有 GDI、PrintWindow 或显示器截图回退。
- [x] 4.2 更新手动识别的目标校验与失败处理，测试遮挡目标能够进入 WGC/OCR，而隐藏、最小化、无效及捕获失败目标得到对应处理；保留区域偏移还原和完整客户区帧尺寸报告。
- [x] 4.3 将显式 Hide、目标切换/清除和手动失败接到捕获 `Stop()`，使旧识别/分析结果不能重新显示或重启旧目标；调整 App 退出顺序为停止自动采样、释放捕获资源、最后关闭日志。
- [ ] 4.4 用户验收：验证合并遮挡比例超过 30% 期间更新最新识别会话、浮层/popup/selector 保持隐藏，30% 及以下继续显示或恢复最新结果；覆盖只失焦仍显示、临时隐藏不停止捕获、显式 Hide 清理会话的区别，不改变 owned-window 和 hover 规则。

## 5. Integrate automatic sampling

- [x] 5.1 更新 `AutoRecognitionController` 使用 WGC 资格和区域采样，保留默认 300ms、最小 100ms、默认关闭和首次手动识别后启动的设置行为；测试遮挡时继续、隐藏/最小化时暂停和 WGC 回调不直接触发 OCR。
- [x] 5.2 为差异基线记录目标、区域、尺寸及 `CaptureGeneration`，在不兼容或失败后清空并以首份有效样本重建；测试单纯遮挡保留基线、静态画面不触发 OCR、恢复初始化不误报新台词。
- [x] 5.3 验证变化后仍执行 OCR 文本确认，相同文本不新增 LLM 请求，变化文本继续全变/增量和句子缓存流程；覆盖自动失败有限重试、取消、关闭设置以及停止后不重启旧任务。

## 6. Verify delivery and document spec ordering

- [x] 6.1 运行相关 Core、Platform.Windows 和 App 测试，再执行 `dotnet build KotobaSenpai.slnx` 与 `dotnet test KotobaSenpai.slnx`；记录实际通过情况和环境限制，不以未执行的桌面验证替代测试结论。
- [ ] 6.2 用户验收真实 HWND：变化测试窗口、实际游戏及一个 Chromium/Electron 或普通桌面应用，覆盖完全遮挡、自己的浮层不入图、区域 OCR 对齐、缩放/最大化、跨 DPI、最小化恢复、目标关闭/切换和连续启动停止；记录 OS、GPU/驱动、窗口模式、结果和资源是否持续增长。
- [x] 6.3 在变更目录补充交付验证记录，说明系统捕获边框、目标后台停绘及最小化范围；仅使用现有显式诊断开关保存必要测试图像，不新增默认截图持久化。
- [ ] 6.4 在本变更同步/归档前，先将已完成 `auto-text-change-detection` 的 `auto-recognition` 基线同步到主规范，再应用本次 MODIFIED delta；确认不会由旧变更再次写回“遮挡时暂停”，不改写历史 archive，也不自动归档其他变更。
- [x] 6.5 运行 `openspec validate replace-gdi-with-wgc --strict`，核对最终实现、五项能力规范和验证证据一致后再进行本变更的同步/归档。
