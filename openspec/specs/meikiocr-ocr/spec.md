# meikiocr-ocr Specification

## Purpose
TBD - created by archiving change replace-ocr-with-meikiocr. Update Purpose after archive.
## Requirements
### Requirement: Local ONNX OCR engine
系统 SHALL 使用本地 ONNX meikiocr 引擎对捕获帧执行日语 OCR，通过 ONNX Runtime 加载文本检测与识别模型。引擎 SHALL 在无网络环境下完成推理，不得依赖 Windows 系统 OCR 语言包。

#### Scenario: Bundle models are present
- **WHEN** 3 个 .onnx 模型（检测、横排识别、竖排识别）随发布打包且可加载
- **THEN** 引擎成功初始化并可就任意捕获帧执行日语 OCR

#### Scenario: Required model is missing
- **WHEN** 任一必需的 .onnx 模型文件在程序目录或环境变量指定目录中缺失
- **THEN** 引擎不伪造识别结果，返回可操作、指明缺失模型与放置方式的错误

### Requirement: Character-level recognition output
系统 SHALL 对每个检测到的文本行返回逐字符的识别结果，每个字符包含原文、字符框和置信度。系统 SHALL 按区间对候选字符执行非极大值抑制，去除重叠误检，并输出按阅读序列排序的字符列表。

#### Scenario: Recognize characters in a line
- **WHEN** 检测器定位到一行文本且识别模型返回该行字符
- **THEN** 系统返回按阅读顺序排列的字符列表，每个字符带有非空文本、字符框坐标和置信度

#### Scenario: Overlapping character candidates
- **WHEN** 识别模型对同一位置返回多个重叠的候选字符
- **THEN** 系统按置信度与非极大值抑制规则保留非重叠的字符，丢弃被抑制的重叠项

### Requirement: Horizontal text recognition
系统 SHALL 对每个检测到的文本行判定横排或竖排，并**仅对横排文本行（宽 ≥ 高）执行识别**。横排文本按自左向右阅读，字符按 x 坐标升序排列。竖排文本行（高 > 宽）第一阶段 SHALL 被跳过，不执行识别、不产生识别结果。

#### Scenario: Horizontal text line
- **WHEN** 文本行宽大于等于高
- **THEN** 系统使用横排识别模型，字符按 x 坐标升序排列

#### Scenario: Vertical text line is skipped in first phase
- **WHEN** 文本行高大于宽
- **THEN** 系统不调用竖排识别模型，不产生该行的识别结果或字符框

<!-- 第二阶段（后续变更）：竖排支持。加载竖排识别模型（32x480），竖排字符按 y 升序、行在段落中按 x 降序（从右到左）；竖排段超过模型高度上限（480px）时按重叠分段识别后合并。-->

### Requirement: Reading order across lines
系统 SHALL 将识别出的横排文本行按阅读顺序排列后返回：横排行按 y 坐标升序，行内字符按 x 坐标升序。竖排行不参与本阶段排序（见竖排第二阶段注释）。

#### Scenario: Order horizontal lines
- **WHEN** 捕获帧包含多行横排文本
- **THEN** 系统按自上而下的顺序返回这些行

