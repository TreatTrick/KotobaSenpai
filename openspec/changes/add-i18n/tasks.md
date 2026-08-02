## 1. Core localization port and error codes

- [x] 1.1 Add `Core/Localization/IStringLocalizer.cs` port: `string Get(string key, params object[] args)` and `event EventHandler? CultureChanged`.
- [x] 1.2 Add `Core/Localization/ErrorCodes.cs` static class with const string keys for every user-facing error: `OcrLanguagePackMissing`, `OverlayTargetNotSpecified`, `FramePixelDataTooShort`, `WindowEnumerationFailed`, `RecognitionFailed`.
- [x] 1.3 Add `Core/Localization/IUserFacingException.cs` marker interface exposing `string ErrorCode` so the presentation resolver can detect translatable exceptions generically.

## 2. Attach error codes to exceptions

- [x] 2.1 Add `ErrorCode` to `IBusinessRule` and `BusinessRuleValidationException`; implement `IUserFacingException` on the exception; set `ErrorCodes.OverlayTargetNotSpecified` in `OverlayTargetMustBeSpecifiedRule`.
- [x] 2.2 Add `ErrorCode` to `WindowsPlatformException` and implement `IUserFacingException`; throw with `ErrorCodes.OcrLanguagePackMissing` in `WindowsOcrWordRecognizer` (replace the Chinese literal).
- [x] 2.3 Replace the Chinese `ArgumentException` in `CapturedFrame` with a code-bearing domain exception (e.g., `InvalidFrameException : ArgumentException, IUserFacingException` carrying `ErrorCodes.FramePixelDataTooShort`).

## 3. App localization infrastructure

- [x] 3.1 Create `App/Resources/Strings.resx` (English neutral) and `Strings.zh-CN.resx` with keys for all XAML labels, ViewModel status messages (with `{0}` placeholders), and one message per `ErrorCodes` key; configure the csproj to embed resources (`EmbeddedResource` + `<Generator>`).
- [x] 3.2 Implement `App/Localization/ResourceManagerStringLocalizer.cs` implementing `IStringLocalizer`: wraps `ResourceManager`, applies/reads current culture, raises `CultureChanged` on switch.
- [x] 3.3 Implement `App/Localization/LocExtension.cs` markup extension: resolves a `Key` via the localizer, subscribes `CultureChanged`, updates bound targets in place; on unknown key displays the key name.
- [x] 3.4 Implement `App/Localization/IUserMessageResolver.cs` + `UserMessageResolver` mapping `IUserFacingException.ErrorCode` to a localized message via `IStringLocalizer`.
- [x] 3.5 Implement `App/Localization/LanguageService.cs`: resolves startup culture (persisted preference -> system Chinese else English), sets `CurrentUICulture` + `DefaultThreadCurrentUICulture`, persists/restores `%LocalAppData%/KotobaSenpai/settings.json`, exposes an `AvailableCultures` list and `CurrentCulture` for binding.

## 4. Wire ViewModel and startup

- [x] 4.1 Update `MainWindowViewModel`: inject `IStringLocalizer` and `IUserMessageResolver`; replace every hard-coded status literal with `localizer.Get(key, args)`; subscribe `CultureChanged` to re-derive `Status` from current state; in `catch` blocks map exceptions via the resolver instead of embedding `ex.Message`.
- [x] 4.2 Update `App.xaml.cs`: invoke `LanguageService` to set culture before the first localized resource is accessed; register `IStringLocalizer`, `IUserMessageResolver`, `LanguageService` in the DI container.
- [x] 4.3 Update `MainWindow.xaml`: replace literal labels/Title with `{Loc Key=...}`; add a language-selection `ComboBox` bound to `LanguageService.AvailableCultures`/`CurrentCulture`.

## 5. Tests and architecture

- [x] 5.1 Update `MainWindowViewModelTests` and any other tests asserting hard-coded Chinese strings to use a localizer fake / expected resource keys and error codes.
- [x] 5.2 Add tests: localizer resolves for active culture and falls back to English neutral; localizer raises/updates on `CultureChanged`; `IUserMessageResolver` maps each `ErrorCode` to a localized message; `LanguageService` default (system Chinese else English) and restore-from-disk logic.
- [x] 5.3 Extend `DependencyDirectionTests`: assert `IStringLocalizer` resides in Core, its implementation resides in App, and ViewModels depend only on the Core interface (not on the App implementation or WPF).
- [x] 5.4 Add a test asserting every localization key referenced in `MainWindow.xaml` and `MainWindowViewModel` exists in the neutral `Strings.resx` (no missing-key gaps).
- [x] 5.5 Run `dotnet build` (with `TreatWarningsAsErrors`) and `dotnet test` for all projects including architecture tests; everything passes.

## 6. Verification

- [ ] 6.1 Manual smoke: launch, switch `zh-CN` <-> `en` at runtime, confirm all static labels, status text, and error messages update without restart.
- [ ] 6.2 Verify persisted language preference restores on next launch; verify clean-launch default follows system language (Chinese system -> `zh-CN`, otherwise `en`).
