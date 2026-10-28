# localization Specification

## Purpose
TBD - created by archiving change add-i18n. Update Purpose after archive.
## Requirements
### Requirement: Supported UI languages

The system SHALL support Simplified Chinese (`zh-CN`) and English (`en`) as user interface languages. English SHALL be the neutral fallback culture: when a translation key is absent for the active culture, the system MUST return the English resource value.

#### Scenario: Both languages are selectable

- **WHEN** the application UI is presented to the user
- **THEN** the user MUST be able to choose between Simplified Chinese and English.

#### Scenario: Missing translation falls back to English

- **WHEN** a localization key has no value defined for the active culture but has an English (neutral) value
- **THEN** the system SHALL return the English value instead of an empty string or the raw key.

### Requirement: Localization port and implementation

Core SHALL define an `IStringLocalizer` port that resolves a resource key (with optional format arguments) to a localized string and that raises a `CultureChanged` event when the active culture changes. The concrete implementation SHALL reside in the App layer; Core and Platform SHALL NOT contain localization resource files or culture-resolution logic.

#### Scenario: Resolve a key for the active culture

- **WHEN** a caller invokes `IStringLocalizer.Get(key)` with the active culture set to `zh-CN`
- **THEN** the returned string MUST be the Simplified Chinese value for that key.

#### Scenario: Substitute format arguments

- **WHEN** a caller invokes `IStringLocalizer.Get(key, args)` with a resource value containing `{0}` placeholders
- **THEN** the returned string MUST have the placeholders substituted with the supplied arguments.

#### Scenario: Culture change raises an event

- **WHEN** the active culture is changed at runtime
- **THEN** `IStringLocalizer` SHALL raise `CultureChanged` so subscribers can refresh.

#### Scenario: Implementation resides in App, port in Core

- **WHEN** the dependency direction is checked by the architecture tests
- **THEN** the `IStringLocalizer` interface MUST reside in `KotobaSenpai.Core` and the implementing class MUST reside in `KotobaSenpai.App`; ViewModels MUST depend only on the Core interface, never on the App implementation or WPF.

### Requirement: Runtime language switching

The system SHALL allow the user to switch the active UI language at runtime and SHALL immediately update all currently displayed localizable text without restarting the application.

#### Scenario: Switching updates XAML labels in place

- **WHEN** the user switches the language from `zh-CN` to `en` while the main window is open
- **THEN** every static label localized via the markup extension MUST update to English without the window being re-created.

#### Scenario: Switching updates ViewModel-bound text

- **WHEN** the active culture changes while a localized ViewModel property is displayed
- **THEN** the ViewModel MUST recompute that property and notify the view so the displayed text updates.

### Requirement: XAML static text localization

Static, non-data-bound text in XAML SHALL be resolved through a localization markup extension bound to a resource key, and SHALL update in place when the culture changes.

#### Scenario: Label displays the localized value

- **WHEN** a XAML element uses the localization markup extension with a valid key
- **THEN** the element MUST display the localized string for the active culture.

#### Scenario: Unknown key is observable

- **WHEN** a XAML element references a key that does not exist in any resource
- **THEN** the system MUST surface the missing key (e.g., display the key name) rather than crash, so the gap is detectable.

### Requirement: ViewModel dynamic text localization

ViewModels SHALL obtain all user-facing strings through `IStringLocalizer` and SHALL NOT contain hard-coded user-facing literals. When `CultureChanged` is raised, ViewModels SHALL re-evaluate currently displayed localized properties.

#### Scenario: Status text comes from the localizer

- **WHEN** the ViewModel sets the status text for the user
- **THEN** the text MUST be produced via `IStringLocalizer` using a resource key and arguments, not an inline literal.

#### Scenario: Status refreshes on culture change

- **WHEN** `CultureChanged` is raised while a status message is displayed
- **THEN** the ViewModel MUST re-derive the status from the current application state and notify the view.

### Requirement: Exception error codes

Core and Platform exceptions whose messages may surface to the user SHALL carry a stable, locale-independent `ErrorCode` instead of localized text. The presentation layer SHALL map `ErrorCode` values to localized user-facing messages; raw exception text MUST NOT be displayed directly to the user as a translated string.

#### Scenario: User-facing exceptions carry an error code

- **WHEN** Core or Platform throws an exception that can reach the UI (e.g., OCR language pack missing, overlay target unspecified, frame data invalid)
- **THEN** the exception MUST expose a non-null, stable `ErrorCode`.

#### Scenario: Presentation layer translates the code

- **WHEN** the ViewModel catches such an exception
- **THEN** it MUST resolve a localized user message from the `ErrorCode` via the presentation-layer mapping, and MUST NOT embed the raw exception message into a localized template as if it were translated text.

### Requirement: Language preference persistence

The system SHALL persist the user's language choice across application restarts and SHALL restore it at startup. When no persisted preference exists, the system SHALL default to Simplified Chinese if the operating system UI culture is Chinese, otherwise to English.

#### Scenario: Persist a user choice

- **WHEN** the user selects a language
- **THEN** the system MUST write that choice to per-user local application data so it survives a restart.

#### Scenario: Restore on startup

- **WHEN** the application starts and a persisted language preference exists
- **THEN** the system MUST apply that preference as the active UI culture before any localized resource is read.

#### Scenario: Default when no preference

- **WHEN** the application starts with no persisted preference
- **THEN** the system MUST default to Simplified Chinese if the OS UI culture is Chinese, otherwise to English.

### Requirement: Startup culture initialization

At startup the system SHALL set `Thread.CurrentThread.CurrentUICulture` and `CultureInfo.DefaultThreadCurrentUICulture` from the resolved preference before any localized resource is first accessed.

#### Scenario: Culture set before first resource access

- **WHEN** the application is starting up
- **THEN** `CurrentUICulture` and `DefaultThreadCurrentUICulture` MUST be configured prior to the first `IStringLocalizer.Get` call or XAML localization resolution.

### Requirement: 界面本地化与模型指令分别处理

`IStringLocalizer` SHALL 继续定义在 Core、实现在 App，并供视图模型及其他需要界面语言文本的消费方使用；Core 和 Platform SHALL 不新增界面本地化资源或界面文化解析逻辑。本地化文本 SHALL 在调用时反映当前界面文化。模型分析指令 SHALL 改为独立的英文模板；模型的回答语言 SHALL 来自句段分析开始时读取的当前生效设置。设置页、弹窗标题、错误和状态提示 SHALL 继续按界面语言本地化。

#### Scenario: 界面消费方按当前语言解析资源
- **WHEN** 界面为英文，消费方通过 `IStringLocalizer.Get` 读取设置说明或状态资源
- **THEN** 返回对应英文资源值；现有中性英文回退规则继续适用

#### Scenario: 切换界面后刷新界面文本
- **WHEN** 界面语言从中文切换为英文
- **THEN** 设置标签、按钮、状态和弹窗标题刷新为英文；分析指令仍使用英文模板，不重新翻译整份提示词

#### Scenario: 回答语言可以独立于界面
- **WHEN** 界面为中文、自定义释义语言为“法语”
- **THEN** 弹窗标题和无释义提示仍为中文，模型的释义和语法说明按法语要求生成
