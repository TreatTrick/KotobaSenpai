## 1. Dependencies & models

- [x] 1.1 在 `KotobaSenpai.Platform.Windows` 添加 `Microsoft.ML.OnnxRuntime` 与 `SixLabors.ImageSharp` 包引用
- [x] 1.2 获取第一阶段需要的 2 个 .onnx 模型（`meiki.text.detect.v0.1.960x544`、`meiki.text.rec.v0.960x32`）放入 `Models/`，配置 `CopyToOutputDirectory=PreserveNewest`（竖排模型 `vertical.32x480` 留待第二阶段）
- [x] 1.3 新增 `THIRD-PARTY-NOTICES` 标注模型来源（rtr46/meiki，LGPL-3.0）与 meikiocr 库来源（Apache-2.0）

## 2. Engine core（移植 meikiocr/ocr.py）

- [x] 2.1 实现 `MeikiOcrEngine`：加载 3 个 ONNX 会话，选择执行提供程序（DirectML 优先、CPU 兜底），镜像 `ocr.py` 构造签名（provider 参数、max_batch_size）
- [x] 2.2 实现检测预处理 `PreprocessForDetection`：BGR→缩放至 960x544 内→零填充→/255→CHW→batch；构建 `orig_target_sizes` 输入
- [x] 2.3 实现检测推理与后处理 `RunDetection`：跑 `(_, boxes, scores)` 输出，conf>0.5 过滤、裁剪到图界、按 y 排序
- [x] 2.4 实现识别预处理 `PreprocessForRecognition`：裁剪 box，横排垫到 960x32（第一阶段仅横排；竖排预处理属第二阶段）
- [x] 2.5 实现识别推理 `RunRecognitionInference`：`images + orig_target_sizes` 输入，按 max_batch_size 分批（第一阶段仅横排识别模型）
- [x] 2.6 实现识别后处理 `PostprocessRecognitionResults`：`chr(label)`、归一化 box 映射回全局坐标、逐字符 NMS（重叠阈 0.3，横排按 x 区间）、标点置信度因子、交换错对修正（`SWAPPED_PAIRS`）
- [x] 2.7 实现 `RunOcr` 编排：检测→**跳过 `h>w` 框**→仅横排识别→合并，输出每行 `{text, chars[]}`（竖排框第一阶段不处理）

## 3. Recognizer 接线

- [x] 3.1 实现 `MeikiOcrWordRecognizer : IWindowWordRecognizer`：捕获帧 Bgra32→BGR→`RunOcr`→每个字符转 `OcrWord`(Text=字符, FrameBounds=字符框)
- [x] 3.2 阅读顺序重排：横排行按 y 升序、行内字符按 x 升序（第一阶段仅横排；竖排排序属第二阶段）
- [x] 3.3 过滤空文本/零面积/越界字符框；全空时返回空 `WordRecognitionResult` 而非抛异常

## 4. 错误码与本地化

- [x] 4.1 Core `ErrorCodes` 移除 `OcrLanguagePackMissing` 或重命名，新增 `OcrModelMissing`、`OcrInferenceFailed`
- [x] 4.2 新增 `WindowsPlatformException` 对应分支，纳入 `IUserMessageResolver` 文案（zh/resource）
- [x] 4.3 本地化资源同步更新 OCR 相关文案键

## 5. DI 替换与清理

- [x] 5.1 `App.xaml.cs:79` 改为注册 `MeikiOcrWordRecognizer` 为 `IWindowWordRecognizer`
- [x] 5.2 删除 `WindowsOcrWordRecognizer.cs` 及其 `Windows.Media.Ocr` 引用
- [x] 5.3 移除 `CapturedFrame` 对 Windows OCR 专用依赖（如有），保留捕获/GDI 不变

## 6. 测试

- [x] 6.1 端到端黄金测试：用固定样例图跑 `MeikiOcrRecognizer`，断言字符框非空、含预期字符、顺序正确（沿 DokiDokiDict `test_japanese_golden.py` 思路）
- [x] 6.2 跳过竖排测试：`h>w` 竖排样例断言不产生识别结果/字符框（不报错）
- [x] 6.3 缺模型测试：模型目录缺失时断言抛 `OcrModelMissing` 且不伪造输出
- [x] 6.4 覆盖层回归：确认词级→字符级后覆盖层逐字下线渲染（`window-word-overlay` 相关测试更新）

## 7. 文档与收尾

- [x] 7.1 更新 `IWindowWordRecognizer`/`OcrWord` 注释为字符级语义
- [x] 7.2 运行 `openspec validate` 通过，`openspec archive` 归档本变更

## 8. 后续变更（第二阶段，不在本次范围）

- [ ] 8.1 加载竖排识别模型 `vertical.32x480`，识别 `h>w` 框
- [ ] 8.2 竖排分段/重叠逻辑（>480px 拆 ≤420px 段 + 64px 重叠，镜像 Python `y_starts`）
- [ ] 8.3 竖排 NMS（按 y 区间）、从右到左排序、竖排黄金测试（超长竖排样例）