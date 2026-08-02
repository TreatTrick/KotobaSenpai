## Context

应用是一个 WPF 桌面日语学习伴侣（.NET 10），采用六边形/DDD 分层：`Core`（纯领域，`net10.0`，零外部依赖）、`Platform.Windows`（适配器，依赖 Core）、`App`（组合根 + WPF 视图/ViewModel，依赖 Core 与 Platform）。架构测试（NetArchTest）钉死依赖方向：Core 不得依赖 Platform/App；ViewModel 不得引用 `System.Windows` 或 Platform.Windows。

当前所有用户可见文案为硬编码简体中文，分布在三处：`MainWindow.xaml` 静态标签、`MainWindowViewModel.Status` 动态文案、以及 Core/Platform 异常消息（后者通过 `catch { Status = $"…{ex.Message}" }` 漏到 UI）。项目路线图将英语母语日语学习者列为后续用户群，且项目文档要求"用户界面以简体中文为主"。

约束：不引入新 NuGet 包（保持 Core 零外部依赖与 BCL-only 风格）；不破坏现有依赖方向；`TreatWarningsAsErrors` 全开。

## Goals / Non-Goals

**Goals:**

- 原生支持简体中文与英语，运行时即时切换、无需重启。
- 本地化抽象作端口位于 Core、实现位于 App，保持领域纯净与可测试性。
- 领域/平台异常通过稳定错误码由表现层翻译，Core/Platform 不含本地化资源或 culture 解析逻辑。
- 语言偏好跨重启持久化；启动时按"持久化偏好 -> 系统中文则简体中文，否则英文"解析默认。
- 不破坏架构测试；新增测试覆盖 localizer、错误码映射、运行时切换与文化回退。

**Non-Goals:**

- 不在本变更引入完整设置系统（SQLite 设置、API Key 存储等）；语言偏好用最小 JSON 持久化，待设置模块落地后迁移。
- 不本地化日志字段、代码标识符、API 名称与日文原文（按项目文档保留原文）。
- 不新增第三种语言；多语言扩展由后续变更处理（但抽象须不阻碍扩展）。
- 不本地化覆盖层 `WpfOverlayRenderer`（只画横线、无文本）。
- 不改变 `window-word-overlay` 能力的需求行为。

## Decisions

### D1：资源存储用 .resx + `ResourceManager`（BCL），不引入 NuGet

**选择：** `KotobaSenpai.App/Resources/Strings.resx`（英文，中性回退）+ `Strings.zh-CN.resx`。

**理由：** .resx 是 .NET 原生资源格式，`System.Resources.ResourceManager` 与 `CultureInfo` 属 BCL，无需 PackageReference，契合 Core 零外部依赖与"不 cargo-cult"的项目原则；ResourceManager 自带 culture 回退链（active → neutral）；VS 有设计器与强类型生成类支持。

**备选：** `Microsoft.Extensions.Localization` 的 `IStringLocalizer`——偏 ASP.NET Core，对双语言桌面应用过重且新增 NuGet，否决。自建 JSON 字典——丢失 ResourceManager 的 culture 解析与回退，等于重造轮子，否决。

### D2：`IStringLocalizer` 端口在 Core，`ResourceManagerStringLocalizer` 实现在 App

**选择：** 接口放 `KotobaSenpai.Core.Localization`，实现放 `KotobaSenpai.App.Localization`。

**理由：** 与现有端口-适配器风格一致（`IWindowCatalog` 等均在 Core）；ViewModel 只依赖 Core 的 BCL 接口（不碰 WPF），架构测试仍通过；可用 fake 在无桌面测试中验证。

**备选：** 接口放 App（ViewModel 同程序集可用）——能通过架构测试，但 Core 已承载跨切面端口，放 Core 更一致，且未来 Core 服务可复用。选择 Core。

### D3：异常用稳定错误码映射，Core/Platform 不放本地化资源

**选择：** 新增 `Core/Localization/ErrorCodes.cs`（const string 键）；`WindowsPlatformException` 与 `IBusinessRule`/`BusinessRuleValidationException` 增加 `ErrorCode`；表现层 `IUserMessageResolver` 把码映射为本地化消息。

**理由：** 领域保持纯净，不引入 `CultureInfo`/资源文件；错误码 locale 无关、可测试、可枚举；用户措辞归属表现层，便于调整文案而不动领域。语言包缺失等具体引导因此能正确本地化。

**备选：** Core/Platform 各自带 .resx 就地本地化——架构测试合法（ResourceManager 是 BCL），但把本地化引入领域层，概念上不洁，否决。异常保留英文 + UI 通用提示——丢失语言包安装等具体引导，否决。

**破坏性影响：** `WindowsPlatformException` 构造函数与 `IBusinessRule` 接口签名变更（加 `ErrorCode`），属 **BREAKING**，但仅影响本仓库内调用方与规则实现，随本变更一并修改。

### D4：自定义 `LocExtension` 标记扩展实现运行时 XAML 切换

**选择：** App 内自建 `LocExtension : MarkupExtension`，订阅 `IStringLocalizer.CultureChanged`，culture 变化时通知所有已解析目标重新取值。

**理由：** `x:Static` 不响应运行时 culture 变化（需重启，被否决）；`WPFLocalizationExtension` NuGet 增加依赖且较旧；约 50 行自建标记扩展即可在原位更新，零依赖。

**实现要点：** `LocExtension` 返回一个实现 `INotifyPropertyChanged` 的轻量值代理（或用静态 localizer + `WeakEventManager`），culture 变化时触发 `PropertyChanged` 使 WPF 重读绑定。键缺失时显示键名以便发现缺口。

**备选：** `x:Static`（重启生效，被用户决策否决）；`WPFLocalizationExtension`（新增依赖）。

### D5：默认 culture = 系统中文则简体中文，否则英文；英文为中性回退

**选择：** 中性 `Strings.resx` = 英文；启动按"持久化偏好 -> 系统中文则简体中文，否则英文"解析。无持久化偏好（首次启动）时读取系统 UI 语言：为中文则默认简体中文，否则默认英文。

**理由：** "原生支持"两种语言意味着尊重 OS 设置；英文作中性回退是通用约定，也与"日志/异常字段保留原文"一致。

**备选：** 不支持语种回退 zh-CN--对非中文系统用户不友好，否决；中性 = 中文--回退到中文对英文用户不友好，否决。

### D6：语言偏好用最小 JSON 持久化

**选择：** `%LocalAppData%/KotobaSenpai/settings.json` 存语言偏好，启动读取。

**理由：** phase1 尚无设置基础设施；最小 JSON 避免过早引入 SQLite 设置模块；待设置模块落地后迁移。

**备选：** 完全不持久化（每次启动重置，体验差）；注册表（不透明）。选择 JSON。

### D7：ViewModel 订阅 `CultureChanged` 重算已显示本地化属性

**选择：** ViewModel 在构造时订阅 `IStringLocalizer.CultureChanged`，事件触发时按当前应用状态重派生 `Status`（及未来其他已显示本地化属性）并通过 `INotifyPropertyChanged` 通知。

**理由：** 满足运行时切换要求；状态由当前状态机派生，重算逻辑已存在，只需改用 localizer 取值。

## Risks / Trade-offs

- **[LocExtension 边缘情况]** 自建标记扩展在 VS 设计器、绑定错误等场景可能有粗糙边缘 -> 保持实现极简、键缺失时回退为键名而非崩溃；加单元测试覆盖解析与切换。
- **[翻译缺失]** zh-CN 缺某键时静默回退英文，可能被误以为已覆盖 -> 加测试：断言 XAML/VM 中引用的每个键都在中性 resx 存在；可选编译期检查。
- **[错误码漂移]** 新增异常未携带 `ErrorCode` 会绕过本地化 -> 约定 + 测试：凡"用户可见异常类型"须暴露非空 `ErrorCode`，加断言。
- **[CurrentUICulture 全局性]** 运行时改 culture 影响全局 ResourceManager，异步中段的字符串读取可能跨 culture -> 切换在 UI 线程同步完成；localizer 读取廉价，可接受。
- **[中性=英文 vs 默认随系统]** 贡献者可能预期中性为中文 -> 在本设计明确记录；默认 UI 随系统语言（中文则简中，否则英文），仅回退链为英文中性。
- **[持久化位置不漫游]** `%LocalAppData%` 不随账户漫游 -> 对 MVP 可接受；待设置模块迁移时统一处理。

## Migration Plan

- 纯代码变更，无数据/持久化迁移（`settings.json` 此前不存在）。实现后运行 `dotnet build` 与全部测试（含架构测试）。
- 回滚：直接 revert 本变更；无残留 schema。未来 `settings.json` 若格式演进，加版本字段。
- 现有硬编码中文断言改为 localizer fake 或错误码断言，随实现一并更新。

## Open Questions

- 语言是否应同时影响数字/日期格式（CurrentCulture 还是 CurrentUICulture）？建议本变更只动 CurrentUICulture（文本），CurrentCulture 留待设置模块。
- 语言选择器放主窗口 ComboBox，还是等设置窗口落地后移入？本变更用主窗口 ComboBox，后续迁移。
