## 1. UniDic 结合型与变形型字段透传

- [x] 1.1 扩展 `src/KotobaSenpai.Core/Tokenization/Token.cs`，在构造函数末尾增加带默认空字符串的 `aConType` 与 `aModType` 参数并暴露 `AConType`、`AModType` 只读属性（保持全部现有调用方与测试向后兼容，所有代码与注释遵循英文规范）。
- [x] 1.2 更新 `src/KotobaSenpai.Platform.Windows/Japanese/UniDicTokenizer.cs`，将 `MeCabUniDic22Node.AConType` 与 `MeCabUniDic22Node.AModType` 透传到 `Token`。
- [x] 1.3 更新 `src/KotobaSenpai.Core/Japanese/SpanPitch.cs`，新增 `SpanPitchSource.MorphologicalRule`。

## 2. 音韵辅助判定与纯离线形态学音调引擎

- [x] 2.1 在 `src/KotobaSenpai.Core/Japanese/PitchAccent.cs`（Layer 0）新增纯拍级辅助函数：特殊拍判定（`っ`、`ん`、`ー`）与标准东京式无声化母音判定（`k/s/t/h/p` 行 `i/u` 段假名后接清辅音 `k/s/t/h/p` 行）。
- [x] 2.2 在 `src/KotobaSenpai.Core/WordGrouping/MorphologicalPitchRules.cs`（Layer 5）实现 `AModType` 变形引擎（`M1@k`、`M2@k`、`M4@k` 及 `M4@k` 的无声化母音不前移与特殊拍左移保护）。
- [x] 2.3 在 `MorphologicalPitchRules` 中实现 `AConType` 左至右递推结合引擎：支持付属語 `F1`–`F6`（含 `助動詞-タ` 单拍末拍平板归一与动词助动词链 `Category` 保持）、接尾辞/複合語 `C1`–`C5`（含 `C3` 特殊拍左移与类别更新）及确定性接頭辞 `P1`/`P2`/`P6`/`P13`/`P14`，遇到缺字段、未支持标记或越界结果严格失败关闭返回 `null`。

## 3. 接入分层离线解析管线

- [x] 3.1 更新 `src/KotobaSenpai.Core/WordGrouping/SpanPitchResolver.cs`，将 `MorphologicalPitchRules` 接入为 L4（`L1 ManualOverride → L2 Dictionary → L3 CounterRule → L4 MorphologicalRule`），仅在 `!word.HasOverriddenReading` 且 L1–L3 均未命中时调用，并在单 token 场景仅对携带有效 `AModType` 的词触发。
- [x] 3.2 验证既有覆盖层（`OverlayAnnotationLayer`）、弹窗（`PhrasePopupViewModel`）与诊断输出无需改动即可正确消费 `SpanPitchSource.MorphologicalRule`（多 token 活用链按整 span 跨行统一 H/L 着色与圆点绘制）。

## 4. 黄金测试集与端到端验证

- [x] 4.1 新增 `tests/KotobaSenpai.Core.Tests/WordGrouping/MorphologicalPitchRulesTests.cs`，按 k3zi Gist 核对后的黄金用例覆盖：单 token `M1@1`（`食べよう`/`行こう`）、一段/五段 `て`/`た` 形（`食べて`/`食べた`/`見て`/`集めて`/`書いて`/`歩いて`/`飲んで`/`呼んで`/`行った`/`呼んだ`）、`M4` 音韵保护（`つけて`/`助けて`/`ぞんじて`）、多层助动词叠链（`食べます`/`行きます`/`食べません`/`食べませんでした`/`食べない`/`行かない`/`食べなかった`/`行かなかった`/`食べたい`/`食べられる`/`食べさせられる`/`食べさせられた`/`行ったら`/`食べたら`）、い形容词活用（`高かった`/`赤かった`/`面白かった`/`高くて`/`赤くて`/`高いです`/`赤いです`）、能产接辞（`論理的`/`物理学`/`食べやすい`/`食べすぎる`/`食べすぎた`）及失败关闭场景。
- [x] 4.2 扩展 `tests/KotobaSenpai.Core.Tests/WordGrouping/SpanPitchResolverTests.cs`，验证 L1/L2/L3 优先于 L4、`HasOverriddenReading` 时不触发 L4、无 `AModType` 的单 token 保持原路径，以及 `食べて` 由 L4 产出 `[1]` 而非误用 `食べる [2]`。
- [x] 4.3 扩展 `UniDicTokenizer` 集成测试，在本地 UniDic 3.1.0 真实分词结果上端到端验证 `AConType`/`AModType` 透传与代表性活用句的 `SpanPitch` 输出。
- [x] 4.4 运行完整 `dotnet build`、`dotnet test`（含 `KotobaSenpai.Architecture.Tests`）确认零警告、零错误且全部测试通过。
