## Why

覆盖层已能按分词把 OCR 词标出下划线并悬停变色，但悬停本身不提供任何释义——对学习者来说"看到词"却查不了意思。需要接入本地日英词典（JMdict），悬停词时弹出英文释义，完成"识别 → 分词 → 悬停查词"闭环。中文释义是后续（经 AI 或另觅数据），本轮只做英文。

## What Changes

- 新增 **JMdict 英文查词能力**：构建工具把 jmdict-simplified JSON 转成 **SQLite 数据库**（汉字表 + 平假读音表 + 条目，带索引），随发布包捆绑；运行时按需查询，不常驻内存。
- 新增 **悬停查词**：悬停覆盖层某个分词词时，用该 token 的 lemma（辞书形）在词典索引查词，弹出小窗显示释义。
- 新增 **本地词典弹窗**：非点击穿透小窗，定位在词下方，显示`头词 + [读音] + 各义项(词性 + 英文释义)`；鼠标移出词后隐藏。
- 复用 MeCab 已有 lemma/reading，**不需要**去活用引擎（区别于 DokiDokiDict 的贪心前缀方案）。

## Capabilities

### New Capabilities
- `english-dictionary`: JMdict 英文词典的索引构建、加载、按 lemma/读音查词，以及悬停时展示释义弹窗。

### Modified Capabilities
<!-- 覆盖层本身（window-word-overlay）悬停变色机制不变；弹窗是叠加的新组件，不改其需求。 -->
（无既有需求变更）

## Impact

- **Core**：新增 `DictionaryEntry`/`DictionarySense` 模型、`IDictionaryLookup` 契约、`JmdictLookupService`；查词按 `Token.Lemma → OrthBase → Reading → BaseReading` 回退，含片/平假名转换。
- **Platform.Windows**：新增 `DictionaryPopup` 窗口（非点击穿透、定位在词下方）；`JmdictSqliteRepository` 打开捆绑的 .db 按需查询；扩展 `WpfOverlayRenderer` 悬停逻辑——命中词变色同时查词并显示弹窗，移出隐藏。
- **App**：DI 注册查词服务与弹窗；捆绑 .db 的路径解析。
- **构建**：新增构建工具，构建时下载 jmdict-simplified JSON → 生成 SQLite .db 随发布捆绑（运行时离线、仓库不存二进制）。
- **依赖**：新增 **Microsoft.Data.Sqlite**（含 native `e_sqlite3`）；复用已有 `ITokenizer`（lemma 打底）；仅构建工具需要网络。