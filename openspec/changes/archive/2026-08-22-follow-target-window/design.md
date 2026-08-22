## Context

`WindowTarget` currently contains the client-area screen rectangle returned by one `Win32WindowCatalog` enumeration. `WpfOverlayRenderer.Render` and `RegionSelectorWindow.Show` use that snapshot to position a WPF window once, while `WordOverlaySession` stores screen-pixel rectangles for words and phrase parts. The persisted `RecognitionRegion` is already window-relative and normalized, but the active overlay geometry is not.

The change must work with the existing WPF/Win32 adapters and GDI screen capture. It must keep the overlay click-through, keep the region selector interactive, preserve the existing phrase/furigana behavior, and obey the repository rule that diagnostics and code-facing messages are English. The agreed behavior is event-driven tracking with no normal-path polling, no automatic OCR after geometry changes, and no overlay drawn over a window that occludes the selected target.

## Goals / Non-Goals

**Goals:**

- Track one selected HWND with Win32 accessibility events and publish current client-area screen geometry, DPI, visibility, minimization, foreground, and Z-order state.
- Reposition and rescale the overlay and region selector immediately after target-window events.
- Keep active OCR and phrase geometry stable as normalized rectangles relative to the recognition-time client area, then map them to physical screen pixels for each render.
- Preserve the normalized recognition-region setting across target movement, resize, and monitor/DPI changes.
- Hide stale UI during occlusion, minimization, destruction, or tracker failure, and restore the existing session when the target becomes visible again.
- Reject explicit screen-based recognition when the target is not visible, not restored, or is occluded by another window. Rendering still requires the target to be foreground so the dependent UI follows the target's Z order.

**Non-Goals:**

- Re-running OCR when the target moves or resizes; a layout reflow still requires an explicit recognition command.
- Capturing pixels from an occluded or background window with `PrintWindow` or Windows Graphics Capture.
- Persisting selected HWNDs, OCR text, word geometry, phrase meanings, or screenshots.
- Detecting the exact alpha/shape of every partially transparent topmost window; Z-order placement remains the visibility mechanism.
- Adding a permanent polling timer as a fallback for event delivery.

## Decisions

### 1. Use one shared WinEvent tracker

Add a Core port for a target-window tracker and register one singleton Windows implementation in the composition root. The implementation uses `SetWinEventHook` with out-of-context delivery and filters events to the attached HWND, `OBJID_WINDOW`, and `CHILDID_SELF`. It listens for `EVENT_OBJECT_LOCATIONCHANGE`, minimize start/end, object destruction, and foreground changes. The callback performs no WPF work: it records that an update is pending and posts one coalesced refresh to the WPF Dispatcher.

The refresh queries `IsWindow`, `IsWindowVisible`, `IsIconic`, `GetClientRect`, `ClientToScreen`, `GetDpiForWindow`, `GetForegroundWindow`, and the target's Z-order. Querying after the event is intentional because event payload coordinates may be stale and resize events arrive in bursts. Attach is idempotent so selecting the same HWND or refreshing a session does not rebuild hooks; the tracker is detached when the selected target changes or is cleared, and when the application exits.

An event hook is preferred to polling because it has no steady-state work, reacts at the source of a move/resize, and gives minimize/destroy/Z-order transitions the same lifecycle path. A polling fallback is deliberately excluded so a failed tracker cannot silently present stale geometry.

### 2. Keep normalized geometry as the stable coordinate space

Introduce a validated normalized rectangle/value mapping in Core (or extend the existing recognition-region mapping) for rectangles whose origin is the target client area's top-left. At recognition completion, convert each grouped-word rect and phrase-part rect from frame/window coordinates into normalized target-relative geometry using the recognition-time client width and height. Retain text, readings, pitch data, meanings, hover ownership, and segment filtering unchanged.

The active session keeps this normalized geometry plus the baseline target identity. On every tracker snapshot, the renderer maps each normalized rect to a new physical `ScreenRect` using the current client rectangle. It never scales a previously mapped screen rect. This makes a move a pure translation, makes a resize proportional, and avoids cumulative rounding drift. Furigana and pitch markers are recreated from the newly mapped word rects during the same render pass, so their location and DIP size follow the current geometry without separate persisted coordinates.

The recognition region remains the existing serialized `x,y,width,height` normalized setting. The selector stores the normalized value while open, converts it to current pixels for drawing and drag hit-testing, and converts the final pixels back to normalized values on confirmation.

### 3. Share lifecycle state between overlay and selector

Inject the singleton tracker into `MainWindowViewModel`, `WpfOverlayRenderer`, `RegionSelectorWindow`, and the recognition application service. Changing the selected window attaches the HWND (or detaches when cleared) and publishes the initial snapshot. Overlay and selector windows subscribe once for their lifetime; showing, hiding, or rendering only reads the current snapshot and never registers hooks. The application service refreshes that selected snapshot before GDI capture and rejects a target that is invalid, hidden, minimized, or occluded. The overlay and selector apply the stricter foreground/renderability check. This keeps capture gating and rendering decisions based on one authoritative snapshot.

Tracker transitions are handled consistently:

- moving/resizing/DPI change: query and remap;
- minimized: hide the overlay and close or suspend the selector;
- restored and still valid: re-show the current session/selector at the new bounds;
- destroyed or invalid: release the session and require a new window selection;
- occluded by another window: keep the overlay immediately above the target in Z order with `Topmost = false`, so the occluding window covers it naturally.

### 4. Make WPF windows target-relative instead of globally topmost

Remove global WPF `Topmost` behavior for the word overlay and selector. Keep `ShowActivated = false`, click-through styles, and `SWP_NOACTIVATE`; use `SetWindowPos` with the target's Z-order relationship whenever the snapshot changes. The overlay remains non-interactive, while the selector remains interactive. Phrase popups are hidden when the target is not visible and are re-anchored after a successful remap.

This prevents target marks from floating over unrelated windows. A target that returns to the visible/front layer causes the tracker to reapply the target-relative position without requiring a new OCR run.

### 5. Fail closed for stale tracking and screen capture

Hook registration, callback, and teardown errors are caught at the platform boundary and sent to the existing English logger. A registration failure or unrecoverable tracker state hides dependent UI and exposes a retry/reselect path; it does not start a second polling mechanism. A callback exception is isolated to that callback so the app and future events continue.

The explicit recognition command performs the visibility/occlusion guard before invoking GDI capture. This avoids silently recognizing an occluding window. Capturing background content remains a separate future capability.

### 6. Verify with pure geometry tests and platform seams

Add Core tests for normalized conversion, translation-only moves, proportional resizes, multi-line rects, phrase-part mapping, minimum/clamped recognition regions, and DPI-independent physical-pixel mapping. Add tracker tests against a fake event source/snapshot provider for coalescing, lifecycle transitions, attach/detach, and failure handling. Add renderer/selector tests with a fake tracker to assert re-render, hide, restore, Z-order state, and no OCR invocation on geometry events. Keep a small Windows manual smoke test for real HWND movement, resize across monitors, occlusion, minimize/restore, and destruction.

## Risks / Trade-offs

- [WinEvent delivery can be bursty or arrive after a window has already changed again] -> Coalesce Dispatcher work and always query the current HWND state instead of trusting event coordinates.
- [A target application can reflow text when resized, so proportional boxes may no longer match glyphs] -> Do not auto-OCR; document explicit re-recognition as the correction path.
- [Removing global `Topmost` can expose WPF Z-order quirks for tool windows] -> Centralize `SetWindowPos` ordering and cover it with real-window smoke tests; keep `ShowActivated = false` and no-activate flags.
- [A foreground target may still have a transparent or always-on-top obstruction] -> Keep the target-relative overlay policy and occlusion guard; exact alpha/shape detection remains explicitly out of scope.
- [WinEvent hook registration or teardown can fail on unusual desktop/session states] -> Fail closed, log the native error in English, clear stale UI, and provide retry/reselection instead of presenting frozen coordinates.
- [Changing the active session coordinate representation affects existing constructors/tests] -> Keep conversion helpers at the application boundary and preserve the existing public word/phrase semantic data while adding normalized geometry fields in a compatible way.

## Migration Plan

No settings migration is required: existing `RecognitionRegion` values already use the normalized `x,y,width,height` format, and no new persistent window or OCR data is introduced. Ship the tracker and renderer changes together, then manually verify move/resize/occlusion/minimize flows on supported Windows versions. Rollback is code-only and leaves existing settings valid; disabling the feature restores the previous static overlay behavior without deleting user data.

## Open Questions

None. The event source, coordinate space, occlusion behavior, recognition guard, and failure policy were confirmed during design review.
