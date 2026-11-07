# CI 门禁（.github/workflows/ci.yml）

合并请求（PR / MR）必须触发并通过本工作流才允许合入 `main`。工作流**没有** `paths` 过滤器：
任何 PR——包括只改一行注释的 PR——都会触发它。

## 1. 触发条件

| 事件 | 说明 |
| --- | --- |
| `pull_request`（`opened` / `synchronize` / `reopened` / `ready_for_review`） | 目标分支为 `main` 的每个 PR，包含 draft 转正式、以及后续每次 push |
| `push` → `main` | 合并后的回归保护，也用于在分支保护里显示最新状态 |
| `merge_group` | 启用 GitHub Merge Queue 时同样拦截 |
| `workflow_dispatch` | 手动重跑 |

同一分支的重复推送会取消上一轮（`concurrency.cancel-in-progress`），只保留最新结果。

## 2. 任务分级

### 2.1 必需检查（阻塞合并）

| Job（显示名） | 作用 |
| --- | --- |
| `Build` | 全解决方案 Release 编译。源工程全部开启 `TreatWarningsAsErrors`，所以编译失败同时意味着引入了警告 |
| `Test / Core` | 领域逻辑：分词、词典跨度、短语校验、会话状态机、缓存（265 例） |
| `Test / Architecture` | **分层门禁**：Core 不得依赖 `System.IO`/`Http`/WPF；Platform 不得依赖 Presentation/App；ViewModel、Guidance、Onboarding 与 CLI 不得依赖 `Presentation.Wpf`；17 个 Core 切片只能依赖更低层 |
| `Test / Platform.Windows` | Win32/WGC/OCR/LLM 适配器，不引用 WPF 呈现项目 |
| `Test / Presentation.Wpf` | WPF 覆层、短语弹窗、注音布局、区域选择与窗口拾取的独立回归测试 |
| `Test / App` | 表现层与组合根。**已排除**桌面验收 fixture（416 例） |
| `Agent CLI contract` | `dotnet publish` 后执行 `tests/Check-AgentCli.ps1`：能力发现、输入校验、凭据脱敏不泄漏 |
| `Gate` | **唯一建议设为必需检查的状态**：上面任一 Job 不是 `success` 就失败 |

`Gate` 存在的意义：分支保护只需勾选一个检查（`Gate`），新增 Job 时不必逐项维护必需检查列表。

### 2.2 告警但不阻塞

| Job | 说明 |
| --- | --- |
| `Desktop acceptance (not gating)` | 只在 `main` 和手动触发时运行，且 `continue-on-error: true` |

它跑的是 `PhrasePopupThemeTests` —— 一个「桌面验收大 fixture」：真实 WPF 窗口、注册全局热键、
`RenderTargetBitmap` 出图，并断言 `host.IsActive` / 前台窗口等**全局桌面状态**。

**为什么它现在不能拦合并**：无交互会话、前台锁（foreground lock）、DPI、动画开关都会让
「窗口能否被激活」这件事本身不可控。实测同一提交连续 4 次运行失败 3 次（Debug/Release 均复现），
与代码改动无关。把这样的测试设为必需，维护者只会用管理员权限绕过，反而摧毁门禁的可信度。

**升级为必需检查的前提**（做完即可把它并入 `Gate`）：

1. 把 fixture 拆成可独立选择的测试（当前 1 个 `[Fact]` 里塞了主题、引导页、设置页、窗口选择、浮层、热键等 7 组检查）；
2. 激活相关断言加上能力前置判断，例如 `if (host.Activate() && host.IsActive)`，OS 拒绝授予前台时跳过该组；
3. 断言改为「本进程的窗口未成为前台」而不是「全局前台句柄逐字节不变」——后者会被无关桌面活动打断
   （热键测试已按此修正）。

## 3. 本地等价命令

CI 跑的就是这些，本地能过基本就能过：

```powershell
dotnet restore KotobaSenpai.slnx
dotnet build KotobaSenpai.slnx -c Release --no-restore

dotnet test tests/KotobaSenpai.Core.Tests/KotobaSenpai.Core.Tests.csproj -c Release
dotnet test tests/KotobaSenpai.Architecture.Tests/KotobaSenpai.Architecture.Tests.csproj -c Release
dotnet test tests/KotobaSenpai.Platform.Windows.Tests/KotobaSenpai.Platform.Windows.Tests.csproj -c Release
dotnet test tests/KotobaSenpai.Presentation.Wpf.Tests/KotobaSenpai.Presentation.Wpf.Tests.csproj -c Release
dotnet test tests/KotobaSenpai.App.Tests/KotobaSenpai.App.Tests.csproj -c Release --filter "FullyQualifiedName!~PhrasePopupThemeTests"

dotnet publish src/KotobaSenpai.App/KotobaSenpai.App.csproj -c Release -o artifacts/publish
./tests/Check-AgentCli.ps1 -PublishDirectory ./artifacts/publish
```

桌面验收（本地有真实桌面时才有意义）：

```powershell
dotnet test tests/KotobaSenpai.App.Tests/KotobaSenpai.App.Tests.csproj -c Release --filter "FullyQualifiedName~PhrasePopupThemeTests"
```

SDK 版本由仓库根的 `global.json` 固定（`10.0.100`，`rollForward: latestFeature`，
即接受同一 feature band 的更高补丁版本）。CI 通过 `actions/setup-dotnet` 读取该文件安装，
本地与 CI 不会因为 SDK 差异而结论不同。

想模拟一台干净的 CI 机器（没有 `settings.json`、UniDic 词典、OCR 模型）：

```powershell
$env:LOCALAPPDATA = Join-Path $env:TEMP 'kotoba-ci-clean'
dotnet test KotobaSenpai.slnx
```

## 4. 让「必须通过才能合并」真正生效

工作流只能**运行**检查，拦截合并需要仓库设置。二选一。

### 4.1 建议：导入仓库规则集（Ruleset）

仓库里已备好 `.github/rulesets/main-protection.json`，内容为：禁止删除 `main`、禁止强推、
强制走 PR、必需检查 = `Gate` 且分支必须基于最新 `main`。

```powershell
gh api --method POST repos/TreatTrick/KotobaSenpai/rulesets `
  --input .github/rulesets/main-protection.json
```

或网页操作：**Settings → Rules → New ruleset → Import a ruleset**，选择该文件。

审核人数默认是 `0`，便于单人维护者自助合并；需要更严格时把
`required_approving_review_count` 改为 `1`（多人协作时推荐）。

### 4.2 备选：经典分支保护

**Settings → Branches → Add branch protection rule**

- Branch name pattern：`main`
- 勾选 **Require a pull request before merging**
- 勾选 **Require status checks to pass before merging** → 搜索并选中 **`Gate`**
- 勾选 **Require branches to be up to date before merging**
- 勾选 **Do not allow bypassing the above settings**

> 必须先让工作流在默认分支上成功跑过一次，检查名 `Gate` 才会出现在候选列表里。

验证是否生效：

```powershell
gh api repos/TreatTrick/KotobaSenpai/rulesets --jq '.[].name'
gh pr checks <PR编号>     # 应看到 Gate 为 required
```

## 5. 失败排查

- **测试失败**：到对应 Job 页面底部下载工件 `test-results-<Suite>`，内含 TRX。
- **卡住不退出**：Job 使用 `--blame-hang --blame-hang-timeout 10m`，
  超时会把挂起进程的调用栈写进 `TestResults` 工件。
- **`Agent CLI contract` 失败**：多半是 `AgentApiContract` 版本号、命令表被改动，
  或 `KotobaSenpai.Cli` 的发布拷贝目标（`PublishAgentCli`）失效；先本地跑第 3 节命令复现。
- **`Desktop acceptance` 红**：先看是「环境没给前台」还是真的渲染/交互回归——
  下载工件里的 `previews/*.png` 直接看渲染结果。

## 6. 维护约定

- **不要给工作流加 `paths` / `branches-ignore` 过滤器**：跳过即等于没有门禁。
- 新增测试项目时同步加入 `test` matrix，否则它不会被任何 PR 保护。
- 断言里不要读取**全局桌面状态**（前台窗口、焦点、其他进程窗口）；这类断言只能表达环境，
  不能表达产品行为，会持续产生假红。
- 新增第三方包时确认 `THIRD-PARTY-NOTICES` 与之一致（当前缺 WPF-UI、CommunityToolkit.Mvvm、
  Microsoft.Data.Sqlite、SQLitePCLRaw、FluentIcons、JMdict 的署名）。
