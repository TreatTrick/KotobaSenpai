# window-word-overlay Specification

## Purpose
TBD - created by archiving change phase1-window-word-overlay. Update Purpose after archive.
## Requirements
### Requirement: Window selection
系统 SHALL 枚举当前可见的顶层窗口，并允许用户选择一个具有有效窗口句柄的目标窗口。选择结果 SHALL 只保存在当前进程内，不得写入截图或原始窗口内容。

#### Scenario: Select a visible window
- **WHEN** 用户打开窗口选择列表并选择一个可见顶层窗口
- **THEN** 系统返回该窗口的句柄、标题和屏幕边界，并将其设为当前目标窗口

#### Scenario: Ignore unusable windows
- **WHEN** 枚举过程中窗口不可见、已销毁或没有有效客户区
- **THEN** 系统不把该窗口放入可选列表，并继续枚举其他窗口

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

### Requirement: Screen coordinate mapping
系统 SHALL 将捕获帧坐标映射为目标窗口在桌面上的屏幕物理像素坐标，并裁剪到窗口边界内。映射结果 SHALL 在 DPI 缩放为 100%、125% 和 150% 时保持比例正确。

#### Scenario: Map a word to screen coordinates
- **WHEN** 捕获帧大小与目标窗口屏幕矩形已知
- **THEN** 系统将词框按 X/Y 比例平移到屏幕矩形，并返回不超出窗口边界的矩形

#### Scenario: Handle DPI scaling
- **WHEN** 目标窗口使用非 100% DPI
- **THEN** 系统依据实际捕获像素与屏幕物理像素比例转换，而不是直接把 DIP 当成像素

### Requirement: Word underline overlay
系统 SHALL 在目标窗口上方显示透明置顶覆盖层，并为每个分词词在其词框下方绘制一条横线。覆盖层刷新时 SHALL 整体替换当前词列表，隐藏时 SHALL 清除所有横线并恢复点击穿透。当鼠标悬停在某个词的包围盒上时，该词下方整条横线 SHALL 由默认色变为悬停色，其余词的横线保持默认色；鼠标移出后恢复默认色。

#### Scenario: Draw one underline per word
- **WHEN** 当前会话包含一个或多个分词词
- **THEN** 覆盖层为每个分词词绘制一条与词框宽度对齐的横线，线条位于词框底部内侧且至少有 1px 可见线宽

#### Scenario: Refresh and hide overlay
- **WHEN** 用户重新识别窗口或关闭覆盖层
- **THEN** 旧词线不会残留；关闭后覆盖层不可见、不可激活且不拦截鼠标输入

#### Scenario: Change line color on hover
- **WHEN** 鼠标悬停在某个分词词的包围盒上
- **THEN** 该词下方整条横线由默认色变为悬停色，其他词的横线保持默认色

#### Scenario: Restore color on mouse leave
- **WHEN** 鼠标移出该分词词的包围盒
- **THEN** 该词下方横线恢复为默认色

#### Scenario: Overlay stays click-through while hovering
- **WHEN** 鼠标悬停在词热区上触发变色
- **THEN** 覆盖层整体仍不拦截点击，点击事件穿透到下方窗口

### Requirement: Failure reporting
系统 SHALL 将窗口销毁、捕获失败、OCR 不可用和覆盖层创建失败转换为用户可理解的错误状态，并允许用户重新选择窗口或重试当前窗口。

#### Scenario: Capture failure
- **WHEN** 目标窗口被最小化、销毁或 Graphics Capture 返回失败
- **THEN** 系统隐藏覆盖层并显示失败原因与重试入口，不终止应用进程

