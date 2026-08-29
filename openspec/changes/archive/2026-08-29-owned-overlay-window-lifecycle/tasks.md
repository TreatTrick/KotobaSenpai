## 1. Core state and normalized geometry

- [x] 1.1 Change `TargetWindowSnapshot` so `IsRenderable` requires visibility, restored state, and no occlusion, but not foreground focus; retain `IsForeground` for diagnostics and target-state reporting.
- [x] 1.2 Add or extend normalized target-relative geometry for word rects and phrase-part rects, including conversion from the recognition-time client area to the current physical screen bounds without cumulative rescaling.
- [x] 1.3 Update `WordOverlayApplicationService` to keep the latest target snapshot as the capture baseline, reject invalid/hidden/minimized/occluded targets before capture, and leave move/resize/visibility events free of OCR and LLM calls.
- [x] 1.4 Add Core tests for foreground-independent rendering, normalized translation and resize, DPI remapping, session retention during temporary hiding, and session invalidation on target destruction or switching.

## 2. Win32 target tracking and occlusion

- [x] 2.1 Extend `WinEventTargetWindowTracker` hooks and event filtering to include explicit `EVENT_OBJECT_SHOW` and `EVENT_OBJECT_HIDE` while preserving Dispatcher coalescing and exception isolation.
- [x] 2.2 Replace the whole-process occlusion exclusion with explicit registration of overlay-related HWNDs, including registration/unregistration lifecycle and diagnostics; allow the KotobaSenpai main window to count as an occluder.
- [x] 2.3 Keep positive-area rectangle intersection as the occlusion rule and publish current client bounds, DPI, visibility, minimized, foreground, and occlusion state after every coalesced event.
- [x] 2.4 Add platform tests for partial overlap, non-overlap, main-window overlap, auxiliary-window exclusion, show/hide events, event coalescing, target destruction, and hook failures.

## 3. Owned overlay window lifecycle

- [x] 3.1 Configure the WPF overlay HWND as a transparent, click-through, non-activating owned top-level window with the selected Aokana HWND as owner and `Topmost = false`.
- [x] 3.2 Update target-relative Z-order and window bounds on each tracker snapshot without activating the target or overlay, keeping the overlay below unrelated occluding windows.
- [x] 3.3 Separate temporary tracker-driven hiding from explicit user hide: hide the window, stop hover, and close popups while retaining the session for hidden/minimized/occluded targets; clear all session state only for explicit hide, target destruction, or target switching.
- [x] 3.4 Reproject and redraw the retained session from normalized geometry after target restore, movement, resize, or DPI changes, including underlines, furigana, pitch markers, phrase markers, and popup anchors.
- [x] 3.5 Ensure target losing foreground focus alone does not hide or clear a visible, unobstructed overlay session, and register/unregister auxiliary overlay HWNDs for occlusion filtering.
- [x] 3.6 Add renderer tests for owner/topmost configuration, foreground-independent rendering, temporary hide/restore, destruction cleanup, hover cleanup, and no recognition invocation from tracker changes.

## 4. Capture and user-visible failure behavior

- [x] 4.1 Preserve the pre-capture guard and actionable error path for invalid, hidden, minimized, or occluded targets, with English diagnostics and localized user-facing status text.
- [x] 4.2 Verify that target restore, Z-order changes, movement, resize, DPI changes, and explicit show/hide events only reposition or hide/show the existing overlay and never start a new OCR or LLM request.
- [x] 4.3 Update any target-dependent selector or popup lifecycle code that still treats foreground focus as a prerequisite or leaves stale UI after target hiding, occlusion, or destruction.

## 5. Verification

- [x] 5.1 Run focused Core and Windows platform tests, preserving and reviewing the existing `[DEBUG-OVERLAY]` instrumentation and unrelated worktree changes.
- [x] 5.2 Run `dotnet build` and the full test suite.
- [x] 5.3 Run `openspec validate owned-overlay-window-lifecycle --type change --strict` and resolve all delta-spec and scenario-format findings.
- [x] 5.4 Perform a Windows smoke test covering focus switching, partial/full occlusion, KotobaSenpai-main-window occlusion, minimize/restore, hide/show, movement, resize, cross-DPI movement, target destruction, and explicit re-recognition after content reflow.
