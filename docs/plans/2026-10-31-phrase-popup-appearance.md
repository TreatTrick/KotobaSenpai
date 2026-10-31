# Phrase Popup Appearance Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add adjustable background transparency and keep word detail popups compact and clear of their source.

**Architecture:** Extend the existing overlay appearance record and settings view model rather than creating a separate preference service. Render the themed background separately from opaque content. Keep geometry calculations in the popup's existing pure placement function.

**Tech Stack:** C#, .NET 10, WPF XAML, xUnit, resx localization.

### Task 1: Constrain popup geometry

- Update `tests/KotobaSenpai.Platform.Windows.Tests/PhrasePopupPlacementTests.cs` to cover right/left preference, insufficient horizontal room, vertical fallback, negative coordinates, DPI-scaled sizes, and two-thirds height.
- Add a native height assertion to `PhrasePopupViewTests.cs` and adapt mouse transfer checks to horizontal placement.
- Run `dotnet test tests/KotobaSenpai.Platform.Windows.Tests --no-restore --filter FullyQualifiedName~PhrasePopup --verbosity minimal` and confirm the new expectations fail.
- Modify `src/KotobaSenpai.Platform.Windows/Overlay/PhrasePopup.xaml.cs`: cap measurement at `Math.Floor(workArea.Height * 2d / 3) / scale`, use an 8 DIP gap, choose the horizontal side with more space when it fits, and otherwise constrain the popup to available vertical space.
- Run the targeted tests again and confirm they pass.

### Task 2: Persist and apply background transparency

- Add tests to `tests/KotobaSenpai.Core.Tests/Overlay/OverlayAppearanceSettingsTests.cs` and `tests/KotobaSenpai.App.Tests/SettingsPageViewModelTests.cs` for default 0.5, endpoints 0/1, invalid input, invariant serialization, failed saves, reset preservation, and renderer notification.
- Extend `src/KotobaSenpai.Core/Settings/OverlayAppearanceSettings.cs` with `PopupBackgroundTransparency` and `PhrasePopupBackgroundTransparency` storage.
- Extend `src/KotobaSenpai.App/ViewModels/OverlayAppearanceViewModel.cs` with a bounded persisted property and localized save-error state.
- Add a localized slider to `src/KotobaSenpai.App/Views/SettingsPage.xaml`, with strings in both supported resx files and keys in `ResourceKeys.cs`.
- In `PhrasePopup.xaml`, separate `PopupFrame` from its scrollable content while retaining themed background brushes. Apply `1 - transparency` to the background only, and forward appearance changes from `WpfOverlayRenderer.OverlayWindow.ApplyAppearance`.
- Run targeted tests before and after implementation. Test background opacity, opaque text, native scrolling and theme updates with real WPF windows.

### Task 3: Verify integration

- Run `dotnet test KotobaSenpai.slnx --no-restore --verbosity minimal`.
- Inspect generated settings/popup previews when available and run `git diff --check`.
- Review the final diff against the three approved behaviors and report remaining limitations accurately.
