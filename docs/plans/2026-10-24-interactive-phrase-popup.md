# Interactive Phrase Popup Implementation Plan

**Goal:** Keep phrase and word details open while the pointer approaches or interacts with them, with a DPI-scaled exit margin and usable vertical scrolling.

**Architecture:** Keep pointer polling in the overlay window. A small platform presentation helper tracks distance to the actual popup rectangle and whether the pointer has entered it. The popup remains non-activating but receives mouse input; the annotation overlay remains click-through.

**Tech Stack:** C#, WPF, Win32, xUnit.

## Behavior

- Before entry, retain the popup while hovering its source, approaching its edges, or staying still. Dismiss when moving away after leaving the source.
- After entry, retain it until the pointer is more than 10 DIP from its nearest edge, using the popup's current native DPI.
- A dismissal onto the original source stays dismissed until the pointer leaves and re-enters that source.
- Retention takes priority over words geometrically behind the popup. Preserve the existing presentation and scroll position during retention.
- Reset hover state when content changes, the popup hides, or the target/session changes.
- Consume mouse input inside the popup without activating it. Use the existing automatic vertical ScrollViewer when the monitor work area cannot contain the full content.

## Task 1: Pointer retention

1. Add geometry-based cases for approaching, retreating, stationary pointers, initial entry, all edges, corners, negative coordinates, and DPI changes.
2. Add the smallest `PhrasePopupHoverTracker` that passes these cases.
3. Integrate retention ahead of overlay source transitions. Exercise the real polling path with explicit cursor coordinates, including overlapping source words and replacing/clearing sessions.

Files: `Overlay/PhrasePopupHoverTracker.cs`, `Overlay/PhrasePopup.xaml.cs`, `Overlay/WpfOverlayRenderer.cs`, and platform hover tests.

## Task 2: Input and scrolling

1. Change the native popup test to require input interception and non-activation, then verify the existing implementation fails it.
2. Remove the popup's transparent input style and handle `WM_MOUSEACTIVATE` without discarding clicks.
3. Verify long content displays a vertical scrollbar, wheel input scrolls, and replacement resets scrolling and hover retention.

Files: `Overlay/PhrasePopup.xaml.cs`, `Overlay/PhrasePopup.xaml`, `PhrasePopupViewTests.cs`.

## Task 3: Verification and documentation

1. Update `openspec/specs/phrase-popup-presentation/spec.md` to replace the former click-through requirement with the requested interaction.
2. Run focused platform tests, then the solution tests serially using an isolated artifacts directory so the running debug build is unaffected.
3. Review the diff and report results. Leave changes uncommitted until requested.

Verification command:

```powershell
dotnet test KotobaSenpai.slnx --artifacts-path D:/project/KotobaSenpai/.codex/tmp/popup-interaction -m:1 -- xUnit.ParallelizeTestCollections=false
```
