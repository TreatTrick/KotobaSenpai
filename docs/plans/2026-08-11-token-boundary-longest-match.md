# Token-Boundary Longest Match Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 UniDic 形态素转换为受 token 边界约束的非重叠最长词块，并让下划线、悬停查词与诊断共享同一解析结果。

> **更新(2026-08-16):** jmdic 的角色收敛为"合并 + 词头/读音"——它仍提供 token→word 合并、词头与读音(本地确定、离线);悬停弹窗的释义与词性不再来自英文 gloss,改由 LLM 词级输出(`llm-word-meanings` 的 `words[]`)提供。英文释义不再作为弹窗语义来源。

**Architecture:** 在 Core 增加独立的 token-span resolver。它先枚举完整 UniDic token 的连续 surface 候选，批量读取 JMdict，再以左到右贪心方式选择最长、不重叠的 span；对动词/形容词后的活用助动词使用首 token lemma。`WordGroupingService` 只负责把 span 映射到 OCR 字符框，`GroupedWord` 携带已解析 entries，WPF renderer 不再从鼠标字符重新猜词。

**Tech Stack:** C#/.NET 10, xUnit, `Microsoft.Data.Sqlite`, 现有 Core/Platform.Windows 六边形端口。

---

### Task 1: Add span and batch-lookup contracts

**Files:**
- Create: `src/KotobaSenpai.Core/Models/LookupSpan.cs`
- Create: `src/KotobaSenpai.Core/Contracts/ITokenSpanResolver.cs`
- Create: `src/KotobaSenpai.Core/Contracts/IBatchDictionaryLookup.cs`
- Modify: `src/KotobaSenpai.Core/Models/GroupedWord.cs`
- Test: `tests/KotobaSenpai.Core.Tests/TokenSpanResolverTests.cs`

**Step 1: Write the failing model/contract tests**

Cover span surface/reading/offset construction, empty entries, and a fake batch lookup implementing the new contract. Assert the existing two-argument `GroupedWord` construction remains source-compatible through an optional entries value.

**Step 2: Run the focused test to verify it fails**

Run: `dotnet test tests/KotobaSenpai.Core.Tests/KotobaSenpai.Core.Tests.csproj --filter FullyQualifiedName~TokenSpanResolverTests`

Expected: compile failure because `LookupSpan`, the resolver contract, and the batch method do not exist.

**Step 3: Implement the minimal contracts**

Define `LookupSpan` with immutable source tokens, surface, reading, lookup key, entries, UTF-16 start/end offsets. Add a separate `IBatchDictionaryLookup.LookupForms(IReadOnlyCollection<string>)` port so the existing single-token interface remains compatible. Add immutable entries/source-token values to `GroupedWord` while preserving its `(Token, Bounds)` positional record contract.

**Step 4: Run the focused test**

Run the same command; expected: PASS for model construction tests, resolver behavior still pending.

**Step 5: Commit**

```bash
git add src/KotobaSenpai.Core tests/KotobaSenpai.Core.Tests/TokenSpanResolverTests.cs
git commit -m "feat(core): add lookup span contracts"
```

### Task 2: Implement token-boundary candidate resolution

**Files:**
- Create: `src/KotobaSenpai.Core/Services/TokenBoundarySpanResolver.cs`
- Modify: `src/KotobaSenpai.Core/Services/WordGroupingService.cs`
- Modify: `tests/KotobaSenpai.Core.Tests/TokenSpanResolverTests.cs`
- Modify: `tests/KotobaSenpai.Core.Tests/WordGroupingTests.cs`

**Step 1: Add failing behavioral tests**

Use a fake tokenizer and fake batch lookup to cover:

- `で / も / ちゃんと` -> non-overlapping `でも`, `ちゃんと`;
- no `もち` candidate when `も` and `ちゃんと` are separate tokens;
- `そ / し / たら` -> `そしたら`;
- `オール / ラウンダー` -> `オールラウンダー`;
- `なかっ / た` -> surface `なかった`, lookup key `無い`;
- `考え / てる` -> surface `考えてる`, lookup key `考える`;
- punctuation and non-contiguous offsets stop candidate growth;
- an unmatched token is retained as an empty-entry span;
- selected spans never overlap.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/KotobaSenpai.Core.Tests/KotobaSenpai.Core.Tests.csproj --filter FullyQualifiedName~TokenSpanResolverTests|FullyQualifiedName~WordGroupingTests`

Expected: failures/compile errors because resolver and grouping integration are absent.

**Step 3: Implement the resolver**

Collect token segments split by punctuation, whitespace, or offset gaps. Enumerate direct surface candidates only at complete token boundaries, collect token lookup keys in UniDic priority order, call `LookupForms` once, and select candidates left-to-right by longest covered span. Allow only contiguous `助動詞` (and explicit `て/で` connective particles) after an inflecting base token for lemma candidates. Use original token metadata for one-token fallbacks.

**Step 4: Integrate geometry mapping**

Make `WordGroupingService` resolve spans first, then map each span's `[StartOffset, EndOffset)` to the union of OCR character boxes. Preserve punctuation exclusion and invalid-span skipping.

**Step 5: Run focused tests**

Run the commands above; expected: PASS, including all five Japanese examples.

**Step 6: Commit**

```bash
git add src/KotobaSenpai.Core tests/KotobaSenpai.Core.Tests
git commit -m "feat(core): resolve longest words on tokenizer boundaries"
```

### Task 3: Add efficient JMdict batch lookup

**Files:**
- Modify: `src/KotobaSenpai.Core/Contracts/IJmdictRepository.cs`
- Modify: `src/KotobaSenpai.Core/Services/JmdictLookupService.cs`
- Modify: `src/KotobaSenpai.Platform.Windows/Dictionary/JmdictSqliteRepository.cs`
- Modify: `tests/KotobaSenpai.Platform.Windows.Tests/JmdictSqliteRepositoryTests.cs`
- Modify: `tests/KotobaSenpai.Core.Tests/JmdictLookupServiceTests.cs`

**Step 1: Add failing batch tests**

Assert one batch call returns forms from both kanji and reading indexes, de-duplicates entries, supports hiragana/katakana fallback, and returns empty maps for missing DB/forms.

**Step 2: Run focused tests to verify failure**

Run: `dotnet test tests/KotobaSenpai.Platform.Windows.Tests/KotobaSenpai.Platform.Windows.Tests.csproj --filter FullyQualifiedName~JmdictSqliteRepositoryTests` and the Core lookup filter.

Expected: compile failure for the new repository/lookup method.

**Step 3: Implement repository batch query**

Add `FindByForms` and execute kanji/reading `IN` queries on one connection per batch, chunking parameters below SQLite's limit. Return a form-to-entries map and keep the existing single-form methods as compatibility wrappers.

**Step 4: Implement lookup normalization and caching**

Make `JmdictLookupService.LookupForms` query original plus hiragana variants in one batch and map results back to caller keys. Reuse the same ordered token keys for the existing single-token `Lookup` path.

**Step 5: Run focused tests**

Expected: all repository and lookup tests PASS.

**Step 6: Commit**

```bash
git add src/KotobaSenpai.Core src/KotobaSenpai.Platform.Windows tests
git commit -m "perf(dict): batch JMdict candidate lookups"
```

### Task 4: Share resolved entries with overlay and diagnostics

**Files:**
- Modify: `src/KotobaSenpai.Core/Services/WordOverlayApplicationService.cs`
- Modify: `src/KotobaSenpai.Platform.Windows/Overlay/WpfOverlayRenderer.cs`
- Modify: `src/KotobaSenpai.App/Diagnostics/FileDiagnosticReporter.cs`
- Modify: `src/KotobaSenpai.App/App.xaml.cs`
- Modify: `tests/KotobaSenpai.Core.Tests/WindowWordOverlayTests.cs`

**Step 1: Add failing integration assertions**

Assert mapped `GroupedWord` retains dictionary entries, the overlay popup path uses those entries, and diagnostics include the merged surface and lookup key.

**Step 2: Implement wiring**

Register the span resolver and inject it into `WordGroupingService`. Preserve entries while mapping frame coordinates to screen coordinates. Change hover handling to read the span's entries and reading directly; retain a fallback lookup only for legacy words with no attached result.

**Step 3: Run focused integration tests**

Run: `dotnet test tests/KotobaSenpai.Core.Tests/KotobaSenpai.Core.Tests.csproj --filter FullyQualifiedName~WindowWordOverlayTests`

Expected: PASS with merged spans and unchanged coordinate behavior.

**Step 4: Commit**

```bash
git add src tests
git commit -m "feat(overlay): use precomputed word spans for hover"
```

### Task 5: Full verification and diagnostic replay

**Files:**
- Modify: `openspec/specs/word-grouping/spec.md`
- Modify: `openspec/specs/english-dictionary/spec.md`

**Step 1: Extend specs**

Document token-boundary candidates, non-overlap, precomputed lookup, and active-line behavior; explicitly note cross-visual-line spans remain deferred.

**Step 2: Run all tests**

Run: `dotnet test KotobaSenpai.slnx --no-restore`

Expected: PASS in all test projects with zero warnings treated as errors.

**Step 3: Replay the three diagnostic OCR/token samples**

Use a deterministic fake tokenizer/lookup harness (or the existing diagnostic text) to confirm the expected surfaces, and measure recognition-time lookup calls. There must be no frame/tensor copy introduced by the span resolver.

**Step 4: Inspect the final diff and status**

Run: `git diff HEAD~5 --stat` and `git status --short`; retain the user's pre-existing `goal.md` and unrelated changes.

**Step 5: Commit**

```bash
git add openspec/specs
git commit -m "docs: specify tokenizer-boundary dictionary spans"
```
