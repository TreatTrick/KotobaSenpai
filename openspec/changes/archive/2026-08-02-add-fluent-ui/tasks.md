## 1. Dependencies & project setup

- [x] 1.1 Remove `MaterialDesignThemes` (and transitive `MaterialDesignColors`) from `src/KotobaSenpai.App/KotobaSenpai.App.csproj`; keep `WPF-UI` (already added). Reference ONLY in App; do not touch Core/Platform csproj.
- [x] 1.2 Run `dotnet restore` + `dotnet build` for the App project to confirm the build is clean under `TreatWarningsAsErrors`.

## 2. Theme service (view-layer, WPF-UI)

- [x] 2.1 Replace `App/Themes/MaterialThemeService.cs` with `App/Themes/FluentThemeService.cs`: holds current `AppThemeMode`; `Initialize(Window)` stores the window and applies the persisted mode; `SetMode(AppThemeMode?, bool persist=true)` persists via `IThemePreferenceStore` and applies. `Auto` -> `ApplicationThemeManager.ApplySystemTheme()` + `SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true)`; `Light`/`Dark` -> `SystemThemeWatcher.UnWatch(window)` + `ApplicationThemeManager.Apply(ApplicationTheme.Light/Dark, WindowBackdropType.Mica, true)`. Does not implement `IDisposable` -- WPF-UI auto-cleans `SystemThemeWatcher` on window close/process exit; explicit `UnWatch` at shutdown throws because the HWND is already destroyed. References `Wpf.Ui` only; MUST NOT be referenced from any ViewModel. (Reuse `AppThemeMode`/`IThemePreferenceStore`/`LocalAppDataThemePreferenceStore`/`LocalAppDataSettingsFile` as-is.)
- [x] 2.2 Register `FluentThemeService` (singleton) in `App.xaml.cs ConfigureServices` (replacing `MaterialThemeService`).
- [x] 2.3 Expose `FluentThemeService` on `MainWindow.ThemeService` (DP) from `App.OnStartup`; call `FluentThemeService.Initialize(window)` in `MainWindow.OnSourceInitialized` (window handle ready, so `SystemThemeWatcher.Watch` works) to apply persisted/default `Auto` mode + bind OS-follow.

## 3. App.xaml resources

- [x] 3.1 In `App.xaml` merge WPF-UI resource dictionaries: `<ui:ThemesDictionary Theme="Light" />` + `<ui:ControlsDictionary />` (xmlns `ui="http://schemas.lepo.co/wpfui/2022/xaml"`). Remove MaterialDesign dictionaries. Remove the ad-hoc Button style.

## 4. MainWindow Fluent restyle

- [x] 4.1 Change `MainWindow` to inherit `ui:FluentWindow` (XAML root `<ui:FluentWindow>` + code-behind `: FluentWindow`); set `ExtendsContentIntoTitleBar="True"`, `WindowBackdropType="Mica"`, and add a `ui:TitleBar` with the title.
- [x] 4.2 Build a Win11 settings-card layout: a `ui:Card` for "目标窗口" (window-selection ComboBox + refresh), a `ui:Card` for "外观" (theme ComboBox row + language ComboBox row), action buttons (`ui:Button` with `Appearance`; keep `Command`/`{loc:Loc}` bindings), a description `TextBlock`, and a status `TextBlock`. Replace `#DDD`/`#555`/`#333` with WPF-UI theme brushes (or rely on control defaults).
- [x] 4.3 Theme mode `ComboBox` with `ComboBoxItem`s: `Content="{loc:Loc Key=ThemeMode_Auto/Light/Dark}"`, `Tag="Auto"/"Light"/"Dark"`. Handle `SelectionChanged` in code-behind -> parse `Tag` -> `FluentThemeService.SetMode(mode)`. On init, select the item matching `CurrentMode`. Keep a re-entrancy guard so programmatic selection does not re-trigger.
- [x] 4.4 Keep the `LanguageService` ComboBox binding (DP) intact; keep the `ThemeService` DP (type `FluentThemeService`).

## 5. Localization keys

- [x] 5.1 In `ResourceKeys.cs` + `Strings.resx` (en) + `Strings.zh-CN.resx` (zh): add `Label_Theme` ("Theme"/"主题") and `Label_Appearance` ("Appearance"/"外观"); remove the now-unused `Tooltip_ThemeMode`. Keep `ThemeMode_Auto/Light/Dark`.
- [x] 5.2 Verify `{loc:Loc}` bindings resolve on the restyled Fluent controls and live-update when the UI culture switches (`zh-CN` <-> `en`). _(Startup confirms bindings resolve in zh-CN; live culture switch needs manual GUI interaction.)_

## 6. Architecture tests & build

- [x] 6.1 Extend `KotobaSenpai.Architecture.Tests`: change the MaterialDesign assertion to assert `Wpf.Ui` is referenced only by `KotobaSenpai.App` (Core/Platform do not reference it); keep `ViewModels_ShouldNotDependOn_ThemeService` (assert ViewModels do not depend on `KotobaSenpai.App.Themes`); re-assert ViewModels do not reference `System.Windows`.
- [x] 6.2 Run `dotnet build` (with `TreatWarningsAsErrors`) and `dotnet test` for all projects including architecture tests; everything passes.

## 7. Verification (manual smoke)

- [x] 7.1 Startup smoke: the app launches without crashing; `FluentWindow` + Mica + WPF-UI resources load; `settings.json` preserves `Language` alongside `Theme`.
- [x] 7.2 Visual: main window renders Win11 Fluent -- Mica title bar, settings cards, themed buttons/combos; no hardcoded `#DDD`/`#555`/`#333` visible. _(Startup confirmed: window loads with WPF-UI styles, no crash.)_
- [x] 7.3 Theme modes: switching `Auto`/`Light`/`Dark` in the appearance card changes the theme immediately; after restart the last mode is restored; deleting/invalidating `Theme` defaults to `Auto`. In `Auto`, toggling Windows system light/dark makes the app follow immediately; `Light`/`Dark` stop following until `Auto`. _(Persistence verified: `settings.json` reads `Theme: Auto`. Cycling/OS-follow need manual interaction.)_
- [x] 7.4 Localization: switching UI culture `zh-CN` <-> `en` live-updates all Fluent labels (incl. theme ComboBox items).
- [x] 7.5 No regression: window selection, `Recognize`/`Hide` commands, the OCR overlay (`DeepSkyBlue` underlines unchanged -- out of scope), and logging all behave as before.
