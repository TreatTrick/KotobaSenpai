## Why

当前 OCR 使用 Windows 系统引擎 `Windows.Media.Ocr`，其准确率天花板低：按文档字体训练，对游戏/视觉小说的渲染色、花体、竖排和振假名识别差。产品首要考虑是速度与准确率，而参考项目 DokiDokiDict 验证了 meikiocr（本地 ONNX，专为游戏文本优化，含竖排）是更优解。替换后保持本地优先承诺（只发清洗后文本给 LLM，不发截图）。

## What Changes

- **替换 OCR 引擎**：用本地 ONNX meikiocr 引擎取代 `Windows.Media.Ocr`，通过 `Microsoft.ML.OnnxRuntime` 跑 3 个模型（文本检测 `detect 960x544`、横排识别 `960x32`、竖排识别 `32x480`），自行实现检测与识别的前后处理。
- **BREAKING — 输出粒度改为字符级**：`OcrWord` 语义从"词"变为"字符"，每个字符一个词框（对齐 DokiDokiDict 的精确取词方案）。覆盖层逐字绘制下划线。
- **竖排拆到第二阶段**：第一阶段检测出的 `h>w` 竖排框直接跳过，只处理横排；竖排（独立模型 + 分段 + 从右到左排序）作为后续单独变更。
- **移除语言包依赖**：不再要求 Windows 日语 OCR 语言包，删除 `OcrLanguagePackMissing` 错误路径；新增 ONNX 模型缺失/推理失败的错误码。
- **模型打包**：将 3 个 `.onnx` 模型随发布打包，首次识别无需联网下载。

## Capabilities

### New Capabilities
- `meikiocr-ocr`: 基于本地 ONNX meikiocr 引擎的日语 OCR 能力，含文本检测、横/竖排识别、字符级词框与阅读顺序输出。

### Modified Capabilities
- `window-word-overlay`: `OCR words with coordinates` 需求由词级改为字符级；"日语 OCR 语言包缺失"场景改为"ONNX 模型缺失"；新增竖排阅读顺序。

## Impact

- **代码**：`src/KotobaSenpai.Platform.Windows/Ocr/WindowsOcrWordRecognizer.cs` 替换为 meikiocr 实现；`App.xaml.cs` DI 注册改一行；`OcrWord` 模型及覆盖层渲染逻辑适配字符级。
- **依赖**：新增 `Microsoft.ML.OnnxRuntime`；移除对 `Windows.Media.Ocr` 的依赖。
- **资源**：新增 3 个 `.onnx` 模型文件随发布打包。
- **系统**：新增错误码（模型缺失/推理失败），本地化文案同步更新。
- **风险**：meikiocr 的 detect+rec 前后处理需从 Python 移植到 C#，是主要工作量与风险点。