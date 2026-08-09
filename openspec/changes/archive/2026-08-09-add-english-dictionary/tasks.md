## 1. Core 模型与查词服务

- [x] 1.1 新增 `DictionarySense`（Pos、Glosses）与 `DictionaryEntry`（Headword、Reading、Senses）模型（`Core/Models/`）。
- [x] 1.2 新增 `IJmdictRepository` 契约：`FindByKanji(string)` / `FindByKana(string) → IReadOnlyList<DictionaryEntry>`（`Core/Contracts/`）。
- [x] 1.3 新增 `IDictionaryLookup` 契约：`IReadOnlyList<DictionaryEntry> Lookup(Token token)`（`Core/Contracts/`）。
- [x] 1.4 实现 `JmdictLookupService`：按 `Lemma → OrthBase → Reading(平假归一) → BaseReading(平假归一)` 回退查 `IJmdictRepository`，全未命中返回空；含片/平假名归一工具。
- [x] 1.5 单测：lemma 命中；reading 回退（含片假名归一）；全未命中返回空；一处命中多义项返回多条。

## 2. 数据访问（Platform.Windows）

- [x] 2.1 `Platform.Windows` 项目加 **Microsoft.Data.Sqlite** 包引用。
- [x] 2.2 实现 `JmdictSqliteRepository`：打开捆绑 .db，按 lemma/reading 查 `kanji`/`reading` 表 → 组装 `DictionaryEntry`；缺失时返回空不崩溃。

## 3. 弹窗

- [x] 3.1 新增 `DictionaryPopup` 窗口：非点击穿透（`WS_EX_NOACTIVATE`、不激活、置顶）、模糊移除时不拦截下方点击。
- [x] 3.2 弹窗内容：`头词 + [读音] + 各义项(词性 + 英文释义)`；无条目显示读音 + "未收录"。
- [x] 3.3 弹窗定位：词包围盒下方，屏幕边缘钳制到工作区（贴底时改到词上方）。

## 4. 悬停接线

- [x] 4.1 `WpfOverlayRenderer` 注入 `IDictionaryLookup`：悬停词变化时查词并更新弹窗（结果按词缓存，避免每 tick 重复查）。
- [x] 4.2 鼠标移出悬停词后延迟隐藏弹窗（防抖动）；悬停词切换时更新内容不残留。
- [x] 4.3 保持现有变色行为不变，弹窗为叠加。

## 5. 构建工具与 DI

- [x] 5.1 新增构建工具：构建时下载 jmdict-simplified JSON → 生成 SQLite .db（`entries`/`kanji`/`reading` 三表 + 索引）输出到发布目录。
- [x] 5.2 `App.xaml.cs` DI 接线：repository → lookup → renderer/popup；捆绑 .db 路径解析。
- [ ] 5.3 端到端验证：识别 → 悬停词弹英文释义，移出隐藏，点击仍穿透。（部分需 GUI + 词典数据，无法无头验证）