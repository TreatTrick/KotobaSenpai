# Tasks: Follow selected target window geometry

## 1. Core coordinate and tracking contracts

- [x] 1.1 Add a validated normalized rectangle/snapshot model for target-relative overlay geometry, including conversion to physical screen pixels from a current client-area snapshot.
- [x] 1.2 Extend the overlay/session geometry flow so grouped-word rects and phrase-part rects retain normalized target-relative coordinates and their recognition baseline without losing text, hover, pitch, meaning, or segment data.
- [x] 1.3 Add a Core target-tracking port and snapshot state covering HWND, client bounds, DPI, visibility, minimized state, foreground state, and target-relative Z-order visibility.
- [x] 1.4 Update `WordOverlayApplicationService` to convert OCR geometry once into normalized coordinates, consume the current target snapshot for capture/mapping, and avoid remapping from previously scaled screen rectangles.
- [x] 1.5 Add unit tests for translation-only moves, proportional resize, multi-line rectangles, phrase-part mapping, DPI changes, clamping, and cumulative-rounding resistance.

## 2. Win32 event-driven tracker

- [x] 2.1 Implement the Windows tracker with `SetWinEventHook` for location change, minimize/restore, destruction, and foreground events, filtering callbacks to the attached HWND.
- [x] 2.2 Marshal callbacks to the WPF Dispatcher and coalesce bursts; query `IsWindow`, `IsWindowVisible`, `IsIconic`, `GetClientRect`, `ClientToScreen`, `GetDpiForWindow`, foreground state, and Z-order after each coalesced event.
- [x] 2.3 Make tracker attach/detach/dispose idempotent and safe for a shared singleton used by the overlay, selector, and recognition workflow; clean up hooks on target change and application exit.
- [x] 2.4 Implement English diagnostic logging and fail-closed state for hook registration, callback, query, and teardown errors without introducing a normal-path polling fallback.
- [x] 2.5 Add fake-event/snapshot tests for event filtering, capturability state, move/resize, minimize/restore, destruction, attach/detach, and hook failures.

## 3. Word overlay following and Z-order

- [x] 3.1 Inject the shared tracker into `WpfOverlayRenderer`; subscribe once for the overlay window lifetime and render the current normalized session geometry against every target snapshot without attaching during render.
- [x] 3.2 Remove global `Topmost` behavior and place the transparent click-through overlay immediately above the target in Z order while preserving no-activate and hover behavior.
- [x] 3.3 Rebuild underlines, phrase markers, furigana, and pitch markers from remapped word geometry on move/resize/DPI events; preserve existing hover ownership and popup anchoring.
- [x] 3.4 Hide and clear the overlay on occlusion/minimize, restore it when the target becomes visible again, and terminate the session on target destruction or unrecoverable tracker failure.
- [x] 3.5 Cover renderer-facing remapping and lifecycle seams with stable-session and tracker-snapshot tests; leave real WPF window smoke coverage to 6.3.

## 4. Recognition-region selector following

- [x] 4.1 Inject the shared tracker into `RegionSelectorWindow`; subscribe for the selector lifetime, keep the active region normalized, and derive current pixel/DIP bounds from each target snapshot.
- [x] 4.2 Update drag hit-testing, corner clamping, minimum-size enforcement, button placement, and redraw logic to use the latest client-area size while a selection is open.
- [x] 4.3 Remove global `Topmost` behavior and keep the interactive selector immediately above the target in Z order without activation; hide/suspend it during occlusion or minimization and restore it on visibility recovery.
- [x] 4.4 Ensure confirmation persists the unchanged normalized region, and target movement/resizing while selecting cannot corrupt settings or leave a stale selector window.
- [x] 4.5 Cover selector-facing normalized region/clamping behavior with existing Core tests and tracker snapshot tests; leave real WPF interaction coverage to 6.3.

## 5. Recognition gating and user-visible failure paths

- [x] 5.1 Add a pre-capture target-state check that rejects explicit recognition when the HWND is invalid, hidden, minimized, or occluded, without invoking GDI/OCR; overlay rendering still requires foreground state.
- [x] 5.2 Add localized status/error resources and ViewModel handling for the unavailable-target and tracker-retry states while keeping diagnostic and exception strings in English.
- [x] 5.3 Ensure a successful explicit recognition uses the latest target client bounds for region pixels, coordinate mapping, and the normalized session baseline.
- [x] 5.4 Add service-level tests asserting no capture on an unavailable target and the actionable stable error code; UI retry behavior reuses the existing ViewModel error path.

## 6. Verification and documentation

- [x] 6.1 Run `dotnet build` and the full test suite, including existing pitch-accent tests and all unchanged worktree changes.
- [x] 6.2 Run `openspec validate follow-target-window --type change --strict` and fix any delta-spec or scenario-format errors.
- [x] 6.3 Perform a Windows smoke test: move, resize, cross-DPI move, partially/full occlusion, minimize/restore, foreground switching, target destruction, and hook failure/retry.
- [x] 6.4 Update relevant user/developer documentation to describe target-relative tracking, normalized recognition regions, occlusion behavior, and explicit re-recognition after content reflow.
