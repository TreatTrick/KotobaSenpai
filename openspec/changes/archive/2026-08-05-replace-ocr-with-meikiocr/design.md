## Context

当前 OCR 走 `Windows.Media.Ocr`，输出词级框、无竖排、对游戏字体识别差。替换为本地 meikiocr ONNX 引擎（参考 DokiDokiDict 验证过的方案）。meikiocr 是 Python 库，本项目为纯 .NET（WPF + DDD），需将 `meikiocr/ocr.py`（485 行，clone 自 rtr46/meikiocr）的 detect+rec 管线原样移植到 C#，用 `Microsoft.ML.OnnxRuntime` 跑 3 个 .onnx 模型。

现有装配：`App.xaml.cs:79` 注册 `WindowsOcrWordRecognizer` 为 `IWindowWordRecognizer`；Core 端 `OcrWord`/`WordRecognitionResult`/`CoordinateMapper` 为词级语义。帧捕获（GDI/GraphicsCapture）不变，产出的 `CapturedFrame` 为 Bgra32。

## Goals / Non-Goals

**Goals:**
- 用本地 ONNX meikiocr 引擎替换 Windows OCR，输出**字符级**词框（对齐 DokiDokiDict 精确取词）。
- 支持竖排文本，阅读顺序与 DokiDokiDict 一致。
- 保持本地优先：3 个 .onnx 模型随发布打包，首次识别无需联网。
- 移除 `OcrLanguagePackMissing`；新增模型缺失/推理失败错误。

**Non-Goals:**
- 不做词义/振假名/形态学分词（那是后续 NMeCab 层的事，本次只换 OCR）。
- **第一阶段不做竖排**：检测出的 `h>w` 竖排框直接跳过，只处理横排。竖排（独立竖排模型 + 分段 + 从右到左排序）作为**后续单独变更**，见 Open Questions 与 spec 的竖排注释。理由：竖排约占移植量 1/3、风险最集中，且是独立可后加的支线（检测本就输出所有框，跳过 `h>w` 框不影响横排路径）。
- 不引入 Python 运行时或子进程。
- 不做 GPU 显式调优参数暴露——执行提供程序自动选择，保留可调接口但不做 UI。

## Decisions

### D1. 全管线 1:1 移植，不重设计算法
`MeikiOcrEngine.cs` 镜像 `ocr.py` 的方法结构：`RunDetection` → 按 `h>w` 分横/竖 → `ProcessRecognitionPipeline`（横排：批量推理、逐字符 NMS、标点因子、交换错对修正）。重心是**忠实复刻**，不是改进——算法已在 DokiDokiDict 场景验证，任何"优化"都可能改变字符框坐标，破坏取词精度。**第一阶段只走横排支线**：识别阶段跳过 `h>w` 框，竖排识别模型（`vertical.32x480`）不加载。竖排的分段/排序逻辑留待第二阶段变更。
- 备选：重新设计（如用更现代的检测头）→ 拒绝。Spirit 是换引擎不是做研究，YAGNI。

### D2. 图像操作用 ImageSharp，张量手动构造
`cv2.resize(bilinear)`、`np.pad`、`/255` 归一化、CHW 转置、batch 维——ImageSharp 做双线性缩放，其余（pad、归一化、转置）用 `float[]` 手动拼。Bgra32 帧先丢 alpha 变 BGR 再喂引擎。
- 备选：`System.Drawing.Common`（GDI+）→ 拒绝，Windows-only 且资源释放繁琐；ImageSharp 托管、插值语义可控、跨平台。
- 风险注意：ImageSharp 双线性与 OpenCV `INTER_LINEAR` 有亚像素级差异，可能使字符框偏移 1px 内。可接受——NMS 重叠容忍 0.3 阈值 + 下线标注视觉不敏感。见 R1。

### D3. 执行提供程序：DirectML 优先，CPU 兜底
ONNX Runtime 按可用性选 `DmlExecutionProvider`（Windows 全 GPU 可用，CPU 兜底），不预设 CUDA。理由：速度是产品首要考虑，DirectML 在无 NVIDIA 的机器上也能拿 GPU 加速；CUDA 仅当用户有 NVIDIA 时增益更明显，但那是同一套模型、运行时自选，无需我们在代码里硬编码。
- 保留 `provider` 参数接口（对齐 meikiocr 构造签名），默认 null 让运行时自选。

### D4. 模型随发布打包 + 环境变量覆盖
3 个 .onnx 拷入 `Models/` 并 `CopyToOutputDirectory=PreserveNewest`，引擎从程序目录加载。漏模型时抛 `OcrModelMissing`（带可操作文案：如何放置模型文件）。另支持 `KOTOBA_MEIKIOCR_MODEL_DIR` 环境变量覆盖目录（开发/测试用），镜像 DokiDokiDict 的 `_packaged_model_dir`。
- 模型来源：`rtr46/meiki.text.detect.v0` + `rtr46/meiki.txt.recognition.v0`（**LGPL-3.0 开源**，随 DokiDokiDict 分发同源），需在 `THIRD-PARTY-NOTICES` 标注。已核实（2026-08）库与模型均为最新：检测 `detect.v0.1.960x544`（14.5MB，9 个月前）、横排 `rec.v0.960x32`（18.6MB，2026-02 更新过、同名原地覆盖，库自动加载新 checkpoint）、竖排 `rec.v0.vertical.32x480`（12.9MB）。无 meiki v1/meiki2 更新换代管线。
- **速度选项（可选）**：检测模型另有小变体 `detect.v0.1.320x192`（14.1MB，约 1/3 计算量）。若速度优先且可接受检测精度略降，可选用小检测模型 + 标准识别模型组合；默认仍用 960x544 保精度。

### D5. 字符级输出落到既有模型，不改 Core 契约形状
`OcrWord.Text` 从"词"改为"单个字符"，`FrameBounds` 仍为字符框 `ScreenRect`。`WordRecognitionResult` 增加可选的 `IsVertical`（行级）或保持每行独立；`OcrWord` 不背竖排标记，竖排由行排序时处理（见 D6）。`CoordinateMapper.ToScreen` 逻辑不变（仍按比例映射）。
- 这是对 `window-word-overlay` 规范"识别词"需求的语义变更（词级→字符级），spec delta 见对应文件。

### D6. 阅读顺序：按 DokiDokiDict 的段落分组排序
meikiocr 检测框按 bbox 的 y 排序（自上而下）。**第一阶段只处理横排**：横排行按 y 升序；行内字符已按 x 升序（阅读顺序）。竖排行（x 降序、从右到左）属第二阶段。镜像 DokiDokiDict `postprocessing.py` 的分组规则，但不做振假名剥离（那是后续层，见 Non-Goals）。

### D7. DI 一行替换 + 删除旧实现
`App.xaml.cs:79` 改为注册 `MeikiOcrWordRecognizer`；删除 `WindowsOcrWordRecognizer.cs` 及其 `Windows.Media.Ocr` 引用。`GdiWindowFrameCapture` 不变。

## Risks / Trade-offs

- **[ImageSharp vs OpenCV 缩放亚像素差]** → 字符框可能偏移 ≤1px。缓解：NMS 重叠阈 0.3 容忍 + 覆盖层视觉不敏感；再加一个黄金测试（见 Tasks）用固定样例图对拍已知字符，验证偏移在可接受范围。
- **[移植忠实度]** → 误改一步就整体偏。缓解：方法级对应 `ocr.py`，逐函数对着源码写；C# 测试沿 DokiDokiDict 的 `test_japanese_golden.py` 思路做端到端样例断言。
- **[模型授权/体积]** → LGPL-3.0 权重 + ~数十 MB 模型增大发布包。缓解：`THIRD-PARTY-NOTICES` 标注来源与许可；模型作独立拷贝不随源码提交（或按授权要求）。
- **[CPU 无 GPU 时速度]** → 纯 CPU 推理变慢。缓解：DirectML 默认覆盖绝大多数 Windows GPU；真无加速时接受 CPU 延迟，保留 provider 注入位。
- **[竖排分段复杂]** → **已通过第一阶段砍竖排消除**：竖排（分段/重叠/独立模型/从右到左排序）整体移出第一阶段，作为后续变更。第二阶段接入时再单独拆函数 + 黄金测试覆盖超长竖排样例。

## Migration Plan

1. 新增 `MeikiOcrEngine` + `MeikiOcrWordRecognizer`（字符级），伴随 specs/design 落地。
2. 模型文件放入 Models/，`THIRD-PARTY-NOTICES` 标注。
3. DI 切到新引擎，旧 `WindowsOcrWordRecognizer` 删除。
4. 错误码/本地化文案更新（`OcrLanguagePackMissing` → `OcrModelMissing`/`OcrInferenceFailed`）。
5. 霍金测试：样例图端到端断言字符框非空 + 含预期字符。
6. 回滚：DI 一行切回旧实现即可（旧文件删除前先确保 git 可找回）。

## Open Questions

- **竖排支持** → **已定：拆到第二阶段，第一阶段不做**。检测出的 `h>w` 框第一阶段直接跳过，只处理横排。第二阶段单独变更：加载竖排识别模型（`vertical.32x480`）+ 分段/重叠 + 从右到左排序 + 竖排黄金测试。
- ~~模型文件是否纳入 git 仓库~~ → **已定：仅发布时打包**，不纳入 git（避免仓库膨胀）；开发/测试用 `KOTOBA_MEIKIOCR_MODEL_DIR` 环境变量覆盖模型目录（见 D4）。
- ~~是否需要迁移期保留 Windows OCR 回退~~ → **已定：直接删除**，不保留可切换 provider（YAGNI；回退靠 git 历史）。`WindowsOcrWordRecognizer` 及其 `Windows.Media.Ocr` 引用一并移除（见 D7）。