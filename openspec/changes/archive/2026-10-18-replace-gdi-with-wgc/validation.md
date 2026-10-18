# WGC migration validation

## Ownership and status

The user requested that actual-window validation be performed by them. No further desktop probes are run after that instruction. Successful unit tests do not certify WGC geometry or driver behavior. On 2026-10-18, the user explicitly requested synchronizing and archiving this change together with `auto-text-change-detection`. The task-level user-acceptance items were already checked before that request. The detailed checklist below preserves the delegated checks; this archive does not claim additional agent-run desktop validation.

## Environment and dependencies

- Windows 11, reported version `10.0.26200.0`; .NET SDK `10.0.103`.
- GPU inventory: NVIDIA GeForce RTX 4060 Ti, driver `32.0.16.1714`; IddSampleDriver Device HDR, driver `23.34.25.279`; GameViewer Virtual Display Adapter, driver `15.6.5.199`. The capture device was created using the default hardware adapter; this inventory does not identify which adapter WGC selected.
- Probe window reported DPI `120` (125%). No actual 100%/150% or cross-monitor DPI acceptance has been completed.
- Existing TFM retained: `net10.0-windows10.0.19041.0`. No UI/video framework was added.
- Direct package: `Vortice.Direct3D11` `3.8.3`. Resolved capture dependencies: `Vortice.DXGI`/`Vortice.DirectX` `3.8.3`, `Vortice.Mathematics` `2.1.0`, `SharpGen.Runtime`/`SharpGen.Runtime.COM` `2.4.2-beta`. All declare MIT in their NuGet metadata. Copyright and permission notices are in `THIRD-PARTY-NOTICES`.

## Observations before the user took over manual validation

These are exploratory observations, not acceptance results for the final adapter:

- A green window completely covering the animated red test window contaminated the old GDI capture with green pixels, reproducing the original problem.
- An isolated WGC prototype with a fresh session per acquisition captured the red content and changing frame number while covered. Framed, borderless, maximized, and a partially offscreen negative-X position passed its pixel/crop assertions. This does not validate a monitor to the left of the primary monitor; only visible pixels were asserted in the offscreen case.
- That prototype captured the visible, restored `Aokana EXTRA2` client at `1480x832` while completely covered. Exact green occluder pixels: `0`; the captured image changed while covered. Background rendering is an observation about that game/session, not a WGC guarantee.
- The initial persistent production adapter passed the framed and borderless cases, then showed a nine-pixel vertical error after changing decorations and maximizing at 125% DPI. Client geometry and raw extent alone did not detect that content offset. Repeated captures retained it, so a short repaint delay was not a sufficient explanation.
- The implementation now recreates the entire capture item/session when window styles or DPI change. It never applies a guessed pixel correction. The related upstream [window-style capture-origin issue](https://github.com/robmikh/Win32CaptureSample/issues/26) supports this mitigation. **The corrected production behavior has not been rerun**, as requested by the user.

## Automated evidence

At the migration checkpoint, `dotnet test KotobaSenpai.slnx --no-restore -p:NuGetAudit=false` passed: Core 124, Platform.Windows 122, App 92, Architecture 28; total **366**, zero failed/skipped reported by the runner. Existing dictionary-dependent tests retain their local-dictionary availability checks.

`dotnet build KotobaSenpai.slnx --no-restore -p:NuGetAudit=false` and `dotnet build tools/WgcCaptureProbe -p:NuGetAudit=false` passed with zero warnings/errors. The probe was compiled, not executed after the handover. `openspec validate replace-gdi-with-wgc --strict` passed; the production-source scan found no GDI/PrintWindow/desktop-capture fallback.

Tests cover capture/render eligibility, client/region offsets including negative physical coordinates and synthetic DPI-scaled rectangles, padded-row copying and owned bytes, invalid HWND/pre-cancel/disposal behavior, localized capture errors, automatic baseline invalidation and occlusion retention, late manual/automatic cancellation, dispatcher-delayed publication after Hide/cancel, idempotent ViewModel shutdown cancellation, and unchanged OCR text skipping another LLM call. Synthetic rectangles do not substitute for actual display DPI tests.

Independent code review identified queued UI publication and uncancelled manual work during exit. Both reproductions received failing tests before their fixes and then passed. WPF now checks generation/cancellation on the rendering dispatcher; exit disposes the existing ViewModel before capture/container teardown. UniDic disposal now shares the parsing lock to avoid freeing a tagger during synchronous background grouping.

Following a user report of flashing hover popups, code tracing identified unconditional `OnTargetChanged → RenderCurrent → StopHover → HidePopup` followed by the next 50 ms hover tick reopening the popup. Non-geometry notifications now maintain Z order without rebuilding the overlay or clearing hover. Four regression cases cover identical/reorder snapshots, foreground/title changes, and required move/resize/DPI/restore rebuilding. The first three cases were observed failing with unconditional rebuilding, then all passed with the guard. Actual popup stability remains user-verified; runtime debug traces were unavailable in the existing log.

The user subsequently reported that the overlay did not appear. Initial publication still calls `RenderCurrent` independently of the geometry guard. Added Information-level publication/render/skip/hide diagnostics, transition-only occluder handle/class/bounds logging, and native positioning failure diagnostics because the original log contained only OCR/LLM completion.

The new runtime log confirmed that seven words and seven underlines reached the renderer but were suppressed by `occluded=True`. At that point, the user confirmed that no visible window covered the game and retained the rule that a real overlapping window hides the whole overlay; the later thirty-percent decision below supersedes that rule. Read-only inspection of the reported blocker HWND `590692` identified process `ChatGPT`, class `Chrome_WidgetWin_1`, extended style `0x2800A8`, including both `WS_EX_LAYERED` and `WS_EX_TRANSPARENT`. The old classifier treated that helper's whole `674x1440` bounding rectangle as a solid blocker. Window class alone had been insufficient to identify it as an ordinary Chromium application window.

The tracker now excludes click-through layered helper rectangles without a process/class allowlist; ordinary windows, layered windows without click-through, and tool windows without that combination still participate. Two exact-style regression cases failed before the filter and passed afterward, together with four blocker-preservation cases. This is a window-style classifier, not per-pixel alpha measurement. Chromium's [native occlusion implementation](https://chromium.googlesource.com/chromium/src.git/+/refs/tags/124.0.6340.0/ui/aura/native_window_occlusion_tracker_win.cc) also filters click-through helper windows when finding solid occluders. Actual restoration of this application's overlay remains user acceptance. The agent only read HWND metadata; it did not launch, activate, move or minimize windows, capture desktop images, or change settings.

Restore initially reported existing ImageSharp `3.1.12` security advisories as errors because warnings are treated as errors. To isolate this migration, validation uses `-p:NuGetAudit=false` for the command only; no project configuration disables auditing and ImageSharp was not changed. A fresh audited restore can still fail until that separate dependency issue is resolved.

## Enlarged window and retracted taskbar follow-up

The user reported that enlarging the game through its maximize button hid the overlay, and supplied a screenshot showing a captioned window with desktop icons still visible at the left. This is not exclusive fullscreen. At `16:59:35.726`, the log recorded a successful overlay render with client bounds `(472, 38, 2496, 1404)`, ending at `y=1442`. One millisecond later, the tracker classified `Shell_TrayWnd` HWND `65992`, bounds `(0, 1438, 3440, 60)`, as an occluder and hid the overlay.

Read-only native metadata confirmed a `3440x1440` monitor, the same game client geometry at DPI `120`, and an auto-hidden bottom taskbar with only two physical pixels inside the monitor. `ABM_GETSTATE` returned `ABS_AUTOHIDE`; `ABM_GETTASKBARPOS` reported its expanded position `(0, 1380)-(3440, 1440)`; the actual window was retracted to `(0, 1438)-(3440, 1498)`. `ABM_GETAUTOHIDEBAREX` returned that exact taskbar HWND for this monitor's bottom edge. DWM frame bounds matched the taskbar's outer rectangle, so switching to DWM bounds alone would not fix this case. The query did not activate, resize, capture, or otherwise manipulate any window.

An initial fix excluded a registered system taskbar retracted beyond its own monitor with at most two physical pixels remaining as an activation strip, using the monitor-specific [ABM_GETAUTOHIDEBAREX API](https://learn.microsoft.com/en-us/windows/win32/shell/abm-getautohidebarex). It preserved ordinary positive-area overlap and added both rectangles to diagnostics. This temporary taskbar-specific code was subsequently removed in favor of the user-requested percentage rule below.

For that initial fix, five activation-strip regression cases (all four edges and a negative-coordinate monitor) failed before the classification and passed afterward; five revealed-taskbar cases also passed. Those tests have now been replaced by percentage-based regressions. Actual overlay behavior after recompiling remains user acceptance.

The follow-up solution run passed **376 tests**: Core 124, Platform.Windows 132, App 92, Architecture 28, with no failures or skips. Command: `dotnet test KotobaSenpai.slnx --artifacts-path D:\project\KotobaSenpai\bin\taskbar-validation -p:NuGetAudit=false --verbosity minimal`. A separate isolated solution build also passed with zero warnings/errors, and strict OpenSpec validation passed. The normal build output was locked by the user's running debugger; the agent did not stop it. An initial external temporary output directory caused two source-inspection tests to fail to locate the repository; rerunning under the ignored repository `bin` directory resolved those path assumptions without changing the tests.

## Thirty-percent occlusion threshold

The user subsequently requested that the overlay hide only when coverage exceeds 30%. The current denominator is the full target client area, independent of the OCR region. All eligible higher windows are clipped to that client rectangle, and a sweep over X boundaries merges the Y intervals in each strip to measure their union. The predicate is strictly `ratio > 0.30`: exactly 30% remains renderable. Ordinary small overlaps and the thin taskbar edge now follow this same rule; the prior two-pixel shell exception and its native AppBar bridge were deleted. The click-through layered helper exclusion and auxiliary HWND registry remain in effect.

Eleven percentage regression cases cover 1%, 29%, exactly 30%, 31%, full coverage, oversized rectangles, no/outside/edge-only coverage, disjoint windows whose combined coverage exceeds the threshold, partially overlapping and duplicate windows, clipping, negative coordinates, and areas exceeding 32-bit integer capacity. Seven cases failed with the former any-intersection decision before the union calculation was implemented; all eleven now pass. Diagnostics include the combined ratio and window count when the target crosses into the hidden state.

The latest solution test run passed **377 tests**: Core 124, Platform.Windows 133, App 92, Architecture 28, with no failures or skips, using the repository-local isolated output command above. No application or desktop probe was launched for this change. The real-window behavior, including owner/Z order and hover under partial covering, remains user acceptance.

## Runtime version and overlay diagnostics follow-up

The user reported another missing overlay in the enlarged captioned game and requested more logging. The `18:59:25.456` entry showed 15 underlines and 43 canvas children submitted, followed at `18:59:25.474` by a taskbar occlusion decision and an explicit renderer hide. These entries used the older format without a combined ratio or target rectangle. Read-only module inspection found process `15072` loading `src/KotobaSenpai.App/bin/Debug/net10.0-windows10.0.19041.0/KotobaSenpai.Platform.Windows.dll`, last written at `16:45:03`. That DLL does not contain the `ExceedsOcclusionThreshold` method; the isolated validation DLL does. Consequently this reproduction was running the old any-intersection rule, not the implemented thirty-percent rule. The agent did not stop the user's process or replace its loaded DLL.

The initial Information-level diagnostics identify `diagnosticsVersion=coverage30-v2`, the process ID, loaded assembly path, module version ID, union-area rule, and exact threshold at tracker initialization. Distinct target snapshots include geometry, DPI, validity and capture/render eligibility. An occlusion input change logs the precise combined ratio and each intersecting window's handle, PID, class, extended style, client intersection, exclusion reason and DWM cloaked metadata. Cloaked status is diagnostic only; no additional filtering policy was introduced.

The renderer logs publication source bounds, rejection/clear/Hide reasons, native HWND styles, owner and neighboring Z order, physical window/client bounds, and native/WPF visibility. After layout, a generation-guarded callback logs actual WPF dimensions, DPI, opacity, canvas/content extents and outside/clipped/unmeasured child counts. This observes geometry without forcing layout, capturing pixels, or changing window placement. Repeated identical tracker inputs and 50 ms hover ticks do not emit the new diagnostics; each completed render gets one after-layout sample.

The isolated solution build passed with zero warnings/errors, and its 377 tests passed again after these diagnostic changes. Runtime diagnostic output and the corrected overlay must be checked after the user stops debugging, rebuilds the normal application output, and reproduces the issue. The `Diagnostics initialized` marker confirms that the new component is actually loaded; a successful isolated build alone does not update a different running output directory.

After the user asked how to rebuild, read-only inspection found no application process holding the component. The normal application project and its project references were then rebuilt with `dotnet build src/KotobaSenpai.App/KotobaSenpai.App.csproj -c Debug -t:Rebuild --no-restore -p:NuGetAudit=false`, following a forced restore with command-only auditing disabled. That build passed with zero warnings/errors. The dependency copied into the normal App Debug directory was written at `19:08:17`, matched the rebuilt Windows project's DLL by SHA-256, and contained both `ExceedsOcclusionThreshold` and `LogCanvasDiagnostics`. No application was launched; actual window acceptance remains pending.

Following the user's verbosity report, occlusion evaluations/candidates, target snapshots, WinEvent/refresh notifications, render details, native window/Z-order state and canvas layouts now use Trace (`[TRC]`). They are written only when `MinimumLogLevel=Trace`; Information and Debug filter these details out. Routine publication and lifecycle decisions use Debug; the one-time component/version marker and explicit user Hide remain Information, and warnings/errors retain their severity. No logging configuration or capture/visibility policy was changed.

## Display scale changes during VS Code debugging

The user reported persistent geometrically valid frame timeouts after switching the display from 125% to 200%, including after returning to 100%. The latest OCR diagnostic was a successful `19:47:16` cropped capture; this directory does not record failed acquisition because it is written only after capture and inference complete. The application log was empty. Read-only window metadata identified the running application as process `18352` (`dotnet`), with a system-aware window fixed at DPI `120`, while the game's per-monitor-aware window reported the current DPI (`96` at 100%, then `192` at 200%). No capture session was started by the investigation.

The VS Code launch configuration targeted `KotobaSenpai.App.dll`, bypassing the executable's native application manifest. Reading the existing EXE's embedded manifest as a resource confirmed `PerMonitorV2`. This is a concrete launch-mode defect: system-aware callers can receive DPI-virtualized Win32 geometry while DWM frame bounds remain physical, so a frame can fail the existing client containment/extent checks after a scale change in either direction. See Microsoft's [DPI virtualization guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows#common-pitfalls-win32) and [GetWindowRect documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect).

Debug now starts `KotobaSenpai.App.exe`, preserving the existing build task and working directory so Windows applies the declared per-monitor DPI mode from process startup. This changes no capture crop, timeout or geometry validation policy. The user must end the existing debug process and start a new one; switching DPI or hot reload cannot replace the host executable of a running process. Actual 125% → 200% → 100% capture/overlay acceptance remains pending with the user.

## Hover popup placement at 200% DPI

The user then reported a hover overlay displaced toward the bottom right and sometimes outside the screen at 200%. Code tracing found a separate defect in `PhrasePopup`: physical screen anchors were assigned directly to WPF `Left`/`Top`, which are DIPs. Its boundary checks also mixed those anchors with DIP dimensions and the primary display's `SystemParameters.WorkArea`. Both group and single-word popup paths used this method.

Popup placement now keeps anchors, native window bounds and the anchor monitor's work area in physical pixels, and moves/resizes its HWND without activation or changing Z order. The existing content measurement stays in WPF DIPs. When there is insufficient room below the anchor, the popup moves above it; both axes and dimensions are limited to the selected display's work area, including negative desktop coordinates. One additional native bounds check handles a DPI/size change when moving to another display. Placement details use Trace; failed native queries or positioning use Warning and hide the popup.

Six pure placement cases pass for 100%/125%/200% popup sizes, bottom/right edges, a negative-coordinate display and a popup larger than its work area. These tests exercise geometry only; they do not launch windows or validate actual WPF DPI transitions. Actual popup alignment after rebuilding remains user acceptance.

## Ensure F5 loads the popup fix

The follow-up screenshot still showed a distant popup, but its running process `7332` loaded the old component with MVID `790b8eec-5995-4061-9d6e-ff928f42b307`. The normal App Debug dependency and Windows project DLL were both still written at `19:22:16`, and metadata inspection found no `CalculatePlacement` method. All four launches recorded through `20:50:36` used that same old MVID. The isolated validation component did contain the fix; the screenshot therefore did not exercise the new positioning path.

Executing the existing F5 build command reproduced a restore failure before compilation: `SixLabors.ImageSharp` 3.1.12 audit warnings `NU1902`/`NU1903` became errors under `TreatWarningsAsErrors`. The local VS Code build task now passes `WarningsNotAsErrors=NU1902%3BNU1903`, following [NuGet guidance](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1901-nu1904). Auditing and warning output remain enabled; this override applies to the local task, not project-wide or release settings. Existing dependency advisories remain unresolved. Workspace `debug.onTaskErrors=abort` also stops F5 on an actual build failure instead of continuing into stale output.

After the user stopped Debug, a normal-directory App rebuild with these task properties passed with zero errors and the existing audit warnings. At `20:53:57`, both normal Debug component copies contained `CalculatePlacement`, had MVID `96187b22-b9e9-418b-83d7-170c3cafbd43`, and matched by SHA-256 (`CFCE7257CF9AA1D0F4227A1F32F6C8537DA1FE5A024E80E44E163F4C4091E0EB`). A subsequent solution test run in the normal output directories passed all **388** tests (Core 124, Windows 144, App 92, Architecture 28) with audit warnings retained. No application or test window was launched; actual popup acceptance remains with the user.

## Remove the vulnerable preprocessing dependency

The user requested resolving the ImageSharp advisories. An upgrade to 4.1.3 passed vulnerability auditing but introduced a build-time licensing requirement for Release. After reviewing the options, the user explicitly chose to remove ImageSharp and use Windows-native image processing. The implementation and offline comparison are recorded in [the approved replacement plan](../../../../docs/plans/2026-10-18-remove-imagesharp.md).

OCR now uses owned `System.Drawing` BGRA bitmaps, native high-quality bilinear resize and the existing BGR/CHW tensor and crop-coordinate contracts. WGC capture is unchanged. The ImageSharp package reference, third-party notice and temporary `WarningsNotAsErrors` task argument were removed. Normal NuGet auditing and warnings-as-errors now apply without exceptions; F5 still starts the native EXE and aborts on build failure.

Both normal-directory Debug and Release App rebuilds passed with zero warnings/errors. All **393** solution tests passed (Core 124, Windows 149, App 92, Architecture 28), with local models and a saved two-line diagnostic PNG supplied so the existing OCR inference and batch-chunking checks executed. The dependency audit including transitives reported no known vulnerable packages in any solution project. Actual App Debug output was checked: it matches the rebuilt Windows DLL and contains no Six Labors assembly or runtime dependency. The agent did not launch any application or capture window pixels.

## User acceptance checklist

Run the app normally with a restored target, or optionally run the checked-in probe:

```powershell
dotnet run --project tools/WgcCaptureProbe -- "Aokana EXTRA2"
```

The probe creates animated red and green test windows, changes only its own window's layout, and temporarily covers the named existing application if found. It does not activate or restore that application. An omitted title defaults to `Aokana EXTRA2`. It performs pixel assertions, prints results, and saves no images. The project is a manual tool, outside the solution/automated test runs.

- [ ] First check framed → borderless → maximized → restored transitions. Full-client output must start at the client origin without caption pixels or missing client edges. Recheck the previously observed nine-pixel error at 125% DPI.
- [ ] Repeat at 100%, 125%, and 150%, then across monitors with different DPI and negative desktop coordinates. Saved normalized regions must remain aligned.
- [ ] Start VS Code debugging through the EXE and switch 125% → 200% → 100% while the app remains running. Capture must recover, and crop/overlay coordinates must stay aligned.
- [ ] At 200%, hover a word and a phrase group near each display edge. The meaning popup must stay adjacent to its anchor and inside that display's work area; repeat on a display with different DPI and negative coordinates.
- [ ] With an actual game and one ordinary desktop/Chromium/Electron window, test partial/full covering and focus loss. Occluder text and the mouse cursor must never enter OCR. Record separately whether the source keeps rendering.
- [ ] Set a recognition region: OCR must stay inside it; word positions must retain the full-client offset. Test full-client recognition too.
- [ ] Enable auto recognition after one manual recognition. While coverage exceeds 30%, change source text. Overlay/popups/selector must stay hidden; reducing coverage to 30% or less should reveal the latest recognized result without triggering OCR merely because of the visibility event.
- [ ] Confirm own underlines/furigana/popups are absent from captures, focus loss alone does not hide a clear target, and temporary renderer hides do not stop capture.
- [ ] Hold the cursor still over a recognized word/group while other windows generate focus/Z-order notifications. The popup and highlight must remain stable; moves/resizes/DPI changes and coverage crossing above 30% must still update/hide the overlay correctly.
- [ ] With the ChatGPT click-through helper present, confirm that an otherwise uncovered game shows underlines/furigana. Cover 29%, exactly 30%, and 31% with ordinary windows; the overlay should remain available for the first two cases and hide for the third. Test two disjoint 20% windows and two fully overlapping 20% windows too. Under partial covering, annotations must retain the target-relative Z order.
- [ ] Enlarge the captioned game until it meets the auto-hidden taskbar edge. The activation strip must not hide annotations by itself. A revealed taskbar also contributes only its client intersection area; the overlay hides only if the total coverage exceeds 30%.
- [ ] Minimize/hide/restore, close the target, select/clear/switch targets, and press Hide during capture/OCR/LLM work. Old results must not return or restart polling. Restore establishes a fresh automatic baseline.
- [ ] Repeat start/stop, resize, cancellation and switching many times; watch process memory, GPU memory and handles for sustained growth. Exit while active and check for hanging processes or native errors.
- [ ] Verify the normal system capture border and disabled cursor capture. WGC does not promise minimized, protected, exclusive-fullscreen, secure-desktop, or background-stopped content.

Use only the existing explicit `DiagEnabled=true` setting if screenshots are necessary for diagnosis. It remains off by default. Do not infer capture failure from an all-black or unchanged frame alone.

## Spec synchronization and archive

On 2026-10-18, the user explicitly requested synchronizing and archiving both `auto-text-change-detection` and `replace-gdi-with-wgc`, followed by a commit.

1. Synchronized all five `auto-text-change-detection` capability deltas first, establishing the `auto-recognition` baseline and the cache, settings, and staged-overlay rules.
2. Applied this change's five capability deltas second. The final main spec continues WGC sampling through occlusion and focus loss; only coverage strictly greater than 30% hides dependent UI. Reconciled older hide/refusal clauses and distinguished explicit session termination from temporary visibility hiding.
3. Confirmed that the old occlusion-pause requirement was superseded. Archiving both explicitly named changes removes the older active delta that could otherwise overwrite it in a later sync.
4. Strict validation passed for both changes and all 23 main specifications. Both change task lists are complete. Existing historical archives were preserved; this record adds no agent-run desktop acceptance results.
