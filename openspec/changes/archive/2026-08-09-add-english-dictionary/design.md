## Context

- 覆盖层已能按分词词画下划线并悬停变色：`WpfOverlayRenderer` 用 `DispatcherTimer`(~50ms) 轮询 `GetCursorPos` 对 `GroupedWord.Bounds` 命中测试，维护"当前悬停词"，变色。
- 分词 token 自带 lemma（辞书形）与 reading，如 `受け → lemma 受ける`。这是查词的关键优势。
- 已调研：英文词典数据用 JMdict（21 万+ 词条，CC BY-SA）；jmdict-simplified 提供周更的预构建 JSON。中文释义为本轮 Non-Goal（后续 AI / 另觅数据）。
- 数据获取决策：**构建时下载 jmdict-simplified JSON → 转成紧凑索引随发布捆绑**（运行时离线、仓库不存二进制）。

## Goals / Non-Goals

**Goals:**
- 构建工具：jmdict-simplified JSON → 紧凑索引文件（汉字表 + 读音表 → 条目），随发布捆绑。
- 运行时离线加载索引，按 token lemma（含 reading 回退）查词。
- 悬停分词词 → 本地词典释义弹窗（非点击穿透、定位在词下方、移出隐藏）。

**Non-Goals:**
- 中文释义（本轮不做；后续 AI / 另觅日中数据）。
- 去活用引擎（DokiDokiDict 那套贪心前缀 + 规则去活用——我们已有 MeCab lemma，不需要）。
- 词频排序、汉字词典、例句、AI 解释。

## Decisions

### D1. 数据源与存储：jmdict-simplified JSON → SQLite 数据库
- 用 [jmdict-simplified](https://github.com/scriptin/jmdict-simplified) 的预构建 JSON（字段可读、无 null、周更）作为构建输入。
- 构建工具把条目写入 **SQLite .db**，三表带索引：
  - `entries`（id, headword, reading, senses 序列化）——按 id 主键；
  - `kanji`（headword → entry_id）——按 headword 建索引，走表记查；
  - `reading`（平假读音 → entry_id）——按读音建索引，走读音查。
- **读音统一存平假名**，查词时把片假名归一为平假名（避免片/平两套键）。
- 理由：UniDic 已占 ~775MB 磁盘 / 几百 MB 内存，**不再为低频查词常驻 200MB+ 内存索引**；查词是"一次一个词"的按需场景，SQLite 页缓存即可，数据待磁盘。

### D2. 查词：按 lemma 回退，无需去活用
- Core 服务 `JmdictLookupService`（实现 `IDictionaryLookup`）按 `Token.Lemma → OrthBase → Reading → BaseReading` 回退：
  - Lemma/OrthBase 走 `kanji` 表（表记）；
  - Reading/BaseReading 归一为平假名后走 `reading` 表；
  - 全未命中返回空（不抛异常）。
- 数据访问经 `IJmdictRepository`（Platform 实现走 SQLite），Core 只做键选择 + 归一化 + 组装条目，保持可单测。
- 理由：MeCab 已给出辞书形，查字典直接命中；比 DokiDokiDict 的贪心前缀 + 去活用简单得多。

### D3. 模型与契约（Core，平台无关）
- `DictionaryEntry`（Headword、Reading、`IReadOnlyList<DictionarySense>`）、`DictionarySense`（Pos、Glosses）。
- `IDictionaryLookup.Lookup(Token token) → IReadOnlyList<DictionaryEntry>`。
- `IJmdictRepository`：`FindByKanji(string)/FindByKana(string) → IReadOnlyList<DictionaryEntry>` 的数据访问抽象；Platform 用 SQLite 实现，测试用内存 stub。

### D4. 弹窗：非点击穿透小窗，复用现有悬停
- 新增 `DictionaryPopup`（WPF 窗口）：`ShowInTaskbar=false`、置顶、不激活、**不设点击穿透**（弹窗本身可关闭，但需不阻挡下方窗口点击——用 `WS_EX_NOACTIVATE` 且鼠标移出自动隐藏）。
- 定位在悬停词 `Bounds` 下方，靠近屏幕边缘时钳制到可视区。
- 内容：`头词 + [读音] + 各义项(词性 + 英文释义)`；无条目时显示读音 + "未收录"。
- 触发/隐藏复用 `WpfOverlayRenderer` 的 `GetCursorPos` 轮询：悬停词变化时查词并更新弹窗；移出后延迟隐藏（防抖动）。查询结果按词缓存，避免每 tick 重复查。

### D5. 架构与接线
- **Core**：模型 + `IDictionaryLookup`/`IJmdictRepository` + `JmdictLookupService`（纯逻辑，键选择 + 归一化，可单测）。
- **Platform.Windows**：`JmdictSqliteRepository`（Microsoft.Data.Sqlite 打开捆绑 .db，按 lemma/reading 查询）、`DictionaryPopup` 窗口、`WpfOverlayRenderer` 注入 `IDictionaryLookup` + 弹窗协调。
- **App**：DI 注册 repository → lookup → renderer/popup；捆绑 .db 路径解析；`App.xaml.cs` 加 `Microsoft.Data.Sqlite` 包引用。
- 构建工具独立（`tools/` 或构建脚本），不参与运行时。

## Risks / Trade-offs

- **构建工具需网络**（下载 jmdict-simplified JSON） → 构建时一次性，CI 可缓存产物；产物 .db 随发布捆绑，运行时离线。
- **Microsoft.Data.Sqlite 引入 native `e_sqlite3`**（按 RID 分发） → 稳定成熟，桌面可接受；打包时需含对应 native 库。
- **UniDic lemma 与 JMdict 表记可能不完全一致**（部分词 lemma 形不同于 JMdict 见出し） → 按 reading 回退 + 片/平归一兜底；仍有少量词无命中，显示"未收录"。
- **弹窗定位靠屏幕边缘可能越界** → 钳制到工作区；词贴近屏幕底部时改为词上方。
- **悬停轮询每 50ms 查词** → 按词缓存结果，仅悬停词变化时查一次；SQLite 查询按主键索引，亚毫秒。