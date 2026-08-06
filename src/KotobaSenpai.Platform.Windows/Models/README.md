# meikiocr 模型

本目录存放 meikiocr 本地 ONNX 模型，**仅随发布打包，不入 git**（见根 `.gitignore`）。

第一阶段需要 2 个模型（来源 `rtr46/meiki`，LGPL-3.0，见仓库根 `THIRD-PARTY-NOTICES`）：

| 模型 | 文件 | 用途 |
|---|---|---|
| 文本检测 | `meiki.text.detect.v0.1.960x544.onnx`（14.5MB） | 定位文本框 |
| 横排识别 | `meiki.text.rec.v0.960x32.onnx`（18.6MB） | 识别横排字符 |

第二阶段（竖排）另需 `meiki.text.rec.v0.vertical.32x480.onnx`。

开发/测试时可将模型放入本目录，或用环境变量 `KOTOBA_MEIKIOCR_MODEL_DIR` 指向含上述文件的目录（引擎优先读取该变量）。