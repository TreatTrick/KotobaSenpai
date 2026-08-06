## MODIFIED Requirements

### Requirement: OCR words with coordinates
系统 SHALL 对当前目标窗口执行一次日语 OCR，并为每个非空识别字符返回原文、字符框和相对于捕获帧的坐标。字符框坐标 SHALL 使用非负宽高的物理像素矩形。识别字符 SHALL 按阅读顺序排列（第一阶段仅横排，自左向右；竖排属第二阶段）。

#### Scenario: Recognize Japanese words
- **WHEN** 目标窗口捕获成功且系统具备 meikiocr 本地模型
- **THEN** 系统返回按阅读顺序排列的字符列表，每个字符包含非空文本和字符框坐标

#### Scenario: Japanese OCR language is unavailable
- **WHEN** meikiocr 模型文件缺失或推理失败
- **THEN** 系统不生成伪造字符坐标，并返回可操作、指明缺失模型或失败原因的错误

#### Scenario: Empty or invalid OCR result
- **WHEN** OCR 返回空文本、零面积或越界字符框
- **THEN** 系统过滤无效项；若没有剩余字符则返回空识别结果而不是抛出未处理异常