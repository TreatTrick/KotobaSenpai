# Window capture independent of desktop occlusion

**Research date:** 2026-10-18

**Scope:** Feasibility analysis for KotobaSenpai, a WPF application targeting `net10.0-windows10.0.19041.0`. This report uses Microsoft documentation and official samples. No capture prototype or target-application compatibility test was run.

## Conclusion

Capturing the selected HWND through **Windows.Graphics.Capture (WGC), using `IGraphicsCaptureItemInterop.CreateForWindow`, is the recommended approach**. Microsoft explicitly describes WGC window capture as reading the DWM window surface and working when other windows cover it. This changes the image source from a rectangle of the final desktop to the selected window's content. It is therefore a credible solution for OCR while the target remains open behind another window. [Microsoft winapp screenshot documentation](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/ui-automation#screenshot)

The realistic initial scope is **occluded, non-minimized windows**, with compatibility validation against the actual games and applications used by KotobaSenpai. Neither access to a window surface nor successful frame delivery proves that the target application has produced current content. This is an API integration and lifecycle change, rather than a replacement of one GDI function call.

## Capture methods compared

| Method | Source and occlusion behavior | Fit for this project |
| --- | --- | --- |
| WGC plus `CreateForWindow(HWND)` | Selects one window; Microsoft's documentation describes capture while occluded. Returns D3D-backed frames. | Preferred. Reuse the existing HWND selection and convert frames to the current CPU BGRA representation. |
| `PrintWindow(HWND, HDC, ...)` | Asks the target application to render into a supplied DC. It can avoid reading the foreground occluder, but output depends on target rendering support. It blocks synchronously. | A possible explicitly tested compatibility option, not a universal replacement. |
| `GetWindowDC` plus `BitBlt` | A window DC and GDI rectangle transfer. The documented contract does not promise a complete, current private compositor surface for arbitrary GPU-rendered windows. | Insufficient basis for promising capture independent of occlusion across the supported application set. |
| Desktop Duplication | Receives the current desktop image for a display output. | Does not meet the requirement: cropping that composed image cannot recover content hidden behind an ordinary opaque window. |

Sources: [WGC window targeting](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow), [WGC occlusion behavior](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/ui-automation#screenshot), [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow), [GetWindowDC](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdc), [BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt), and [Desktop Duplication](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api). The suitability judgments are engineering conclusions drawn from those contracts.

`GetWindowDC` is primarily documented for nonclient painting. Merely obtaining it does not establish a general solution to hidden-window capture. `CAPTUREBLT` is also not a recovery flag: its documented purpose is to include layered windows above the window in the result. [Window DC purpose](https://learn.microsoft.com/en-us/windows/win32/gdi/window-display-device-contexts), [CAPTUREBLT](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)

`PrintWindow` normally captures the entire window and offers `PW_CLIENTONLY`. Its documentation says the target receives a print message and draws into the requested DC. Consequently, successful use with one ordinary GDI application does not prove success with WPF, Chromium, DirectX, OpenGL, video, or a particular game engine. This is a compatibility limitation inferred from the target-rendering contract, not a documented list of universally unsupported renderers. Blocking it on a background thread protects the UI, but a managed timeout alone would not cancel an in-progress native call. The current reference does not supply a cancellation or timeout parameter. [PrintWindow contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)

## What WGC establishes, and what remains conditional

### OS and .NET integration

- `CreateForWindow` is supported from Windows 10 version 1903, build 18362. The project's Windows 19041 API target already includes this baseline. [HWND interop minimum](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
- WGC itself predates HWND interop. Microsoft's official WPF sample demonstrates window capture through these APIs; it is evidence of desktop/WPF suitability, not a modern .NET 10 implementation to copy unchanged. [Official WPF sample](https://raw.githubusercontent.com/microsoft/Windows.UI.Composition-Win32-Samples/master/dotnet/WPF/ScreenCapture/README.md)
- For .NET 6 and later, a Windows-version-specific TFM supplies Windows SDK reference assemblies. Keeping the current TFM is sufficient for basic WGC. Native D3D11 and HWND/WinRT interop still need an implementation. A newer API target can be selected separately from a lower supported OS version when runtime guards are used. [Calling WinRT from desktop .NET](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- The HWND route constructs a capture item directly from the selected window; it avoids replacing KotobaSenpai's window list with the system picker. Microsoft describes HWND and monitor interop as otherwise operating like picker-created capture items. A system picker is an alternative, not a prerequisite for this desktop route. [Microsoft capture-team explanation](https://blogs.windows.com/windowsdeveloper/2019/09/16/new-ways-to-do-screen-capture/)

### Occlusion, background operation, and minimized windows

**Documented behavior:** Microsoft's current winapp documentation explicitly identifies WGC as a capture path that works while a window is occluded. This directly supports the principal requirement. However, that documentation describes Microsoft's capture tool; it should not be expanded into a promise that every third-party renderer always updates under every desktop state. [Microsoft WGC capture behavior](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/ui-automation#screenshot)

**Application-dependent freshness:** DXGI documents that an application receiving `DXGI_STATUS_OCCLUDED` may stop rendering until it becomes visible again; that particular status is not returned by flip-model swap chains. This supports a narrower conclusion: some applications may intentionally suspend rendering, and a capture consumer cannot assume new pixels are being produced merely because capture remains active. Other application-specific background throttling must be measured with the actual targets. [DXGI occlusion status](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-status)

**Minimized is a separate requirement:** Microsoft's WPF sample explicitly states that minimized windows are enumerated but not captured. This is sample behavior rather than a complete specification of every OS and renderer combination. Do not promise fresh OCR while minimized; retain the current minimized gate in an initial implementation. Possible missing or stale frames should be treated as a capture-state issue, not recognized repeatedly as new content. [Official sample limitation](https://raw.githubusercontent.com/microsoft/Windows.UI.Composition-Win32-Samples/master/dotnet/WPF/ScreenCapture/README.md)

**Protected/excluded content:** Windows offers display-affinity settings that remove a window from capture or suppress its contents. WGC is not a supported way to override those settings. WDA_EXCLUDEFROMCAPTURE is supported starting at Windows 10 version 2004. Locked sessions, secure desktops, RDP changes, elevated targets, hidden/cloaked windows, and exclusive-fullscreen games remain outside this report's verified application scope. [Display-affinity contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)

### Border and cursor behavior

WGC normally displays a colored capture indicator. `IsBorderRequired` was introduced at build 20348, later than the project's 19041 API target. Microsoft documents user consent through `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` and the `graphicsCaptureWithoutBorder` package capability for disabling it; denied consent leaves the border visible. Borderless capture is therefore a separate optional feature requiring OS/API and deployment investigation. Basic occlusion-independent capture should work with the indicator enabled. [Border property and consent requirements](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired?view=winrt-26100)

`IsCursorCaptureEnabled` is available from build 19041. Disabling cursor capture is appropriate for OCR so that the pointer does not cover text. [Cursor capture property](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.iscursorcaptureenabled?view=winrt-26100)

## Getting a WGC frame into the current OCR input

The proposed adapter flow is:

1. Create/reuse a D3D11 device and its WinRT `IDirect3DDevice` bridge.
2. Create the capture item for the selected HWND, then a frame pool and capture session.
3. Receive frames on a worker thread. `CreateFreeThreaded` removes the `DispatcherQueue` dependency and raises `FrameArrived` on its internal worker; it is available from build 17763. [Free-threaded pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded?view=winrt-26100)
4. Obtain the native texture behind the WinRT frame surface through `IDirect3DDxgiInterfaceAccess`. [DXGI surface interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.directx.direct3d11.interop/ns-windows-graphics-directx-direct3d11-interop-idirect3ddxgiinterfaceaccess)
5. Copy the required rectangle to a CPU-readable D3D11 staging texture, then map it. The documented staging usage supports GPU-to-CPU transfer through copy operations. [Staging resources](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_usage), [mapping](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-map)
6. Copy rows into the contiguous `CapturedFrame` BGRA buffer. Respect the mapped texture's `RowPitch`; it can exceed `width * 4` because of row padding. Unmap and release the native resources after copying. [Mapped resource layout](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_mapped_subresource)

Use BGRA8 for the existing SDR OCR pipeline. Handle frame `ContentSize` rather than reading undefined pixels outside the current content, recreate the pool after resize/device changes, and promptly dispose frames instead of keeping frame-pool buffers checked out. HDR may require a floating-point capture pipeline and tone mapping; include it as a separate validation case. [WGC pixel formats, frame lifetime, size changes, and HDR](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)

### Preserve the existing client-area coordinate contract

Repository analysis supplied alongside this research establishes that `Win32WindowCatalog` and `WinEventTargetWindowTracker.RefreshCore` obtain **client-area** bounds through `GetClientRect` and `ClientToScreen`. The capture region and subsequent OCR/overlay coordinates are based on that client origin. `CreateForWindow` captures a window item, not the project's pre-cropped client region, so forwarding its raw frame unchanged would risk title-bar/border offsets.

For a proposed client rectangle `C` in physical screen coordinates and a validated frame-origin point `F`, convert a client-relative OCR region `R` into the frame rectangle:

```text
frameX = C.left - F.x + R.x
frameY = C.top  - F.y + R.y
frameWidth  = R.width
frameHeight = R.height
```

This formula is a design proposal that assumes a verified 1:1 physical-pixel mapping. Determine the real origin against `GraphicsCaptureItem.Size`, frame `ContentSize`, and DWM bounds for framed, maximized, and borderless windows; reject/retry a mismatched resize sample. Do not assume `GetWindowRect` is always the texture origin: Microsoft documents DPI virtualization and invisible resize borders for that API. `DWMWA_EXTENDED_FRAME_BOUNDS` provides visible bounds without the same DPI adjustment. `ClientToScreen` reports device coordinates. [Window and DWM bounds](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect), [client-to-screen conversion](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clienttoscreen)

Return the same client-relative output semantics and crop offsets currently expected by OCR consumers. Mixed-monitor DPI movement and negative screen coordinates must be tested. No source reviewed here establishes a universal frame-origin equation covering all window styles, so calibration remains an implementation acceptance requirement.

## Repository changes implied by this approach

These are recommendations based on the current repository findings, not implemented changes:

- Add a WGC implementation behind `IWindowFrameCapture` and retain CPU BGRA `CapturedFrame` as the downstream boundary. OCR, tokenization, and dictionary logic can retain their current image-input contract.
- Separate capture eligibility from overlay visibility. Currently `TargetWindowSnapshot.IsCapturable` requires valid, visible, non-minimized, and non-occluded, and `IsRenderable` is equivalent. WGC can relax the occlusion restriction for capture, while overlay rendering should still respect occlusion so that recognized-word overlays do not float above unrelated foreground applications.
- Update both `WordOverlayApplicationService` validation and `AutoRecognitionController` polling gates. Replacing GDI capture alone leaves the existing occlusion checks blocking the new behavior.
- Revise the existing capture requirement and related tests in `openspec/specs/window-word-overlay/spec.md`: its current recognition scenarios explicitly reject occluded targets to protect screen capture. Preserve its separate overlay-hiding behavior when changing the capture requirement.
- Keep a session per selected target and dispose/recreate it on target change, close, relevant capture failure, or shutdown. The existing automatic interval is 300 ms by default, with a minimum of 100 ms; repeatedly creating a session for every poll is an unnecessary design risk. Receive and release frames promptly, retaining an owned latest-frame copy or texture as appropriate, and perform CPU readback/OCR at the requested cadence.
- Track frame availability and capture timestamps separately from OCR completion. A static image legitimately produces identical pixels; missing updates alone cannot distinguish unchanged text from an application that stopped rendering. Reset stale state on target switch, resize, minimize/restore, and session failure.
- If retaining GDI as a fallback, preserve its unobstructed-window gate. Do not silently fall back to screen pixels while the target is occluded: that would reintroduce the original problem while appearing to offer the new guarantee.

## Bounded validation before implementation acceptance

Start with one deliberately changing test window and the user's representative game or application. Add one Chromium/Electron application and one ordinary WPF/Win32 application to expose renderer differences. Record OS build, GPU/driver, target version, window mode, captured timestamp, dimensions, and saved test frame.

| Scenario | Acceptance criterion |
| --- | --- |
| Visible baseline, then inactive without overlap | Same selected window is captured; text remains readable; capture does not steal focus. |
| Another opaque window covers the text region, then covers the entire target | Captured pixels contain only the selected target; changing test content continues to appear if the target continues rendering. Distinguish a frozen target from contaminated capture. |
| KotobaSenpai's overlay over the target | Captured content and OCR do not recursively include the overlay. Overlay remains hidden when the target is behind an unrelated foreground window. |
| Minimize and restore | No false new OCR results while minimized; capture recovers after restore without retaining the old crop or target identity. |
| Resize, maximize/restore, and borderless mode | Client crop and OCR boxes align after each transition; no title-bar or undefined buffer pixels enter the OCR region. |
| 100%, 150%, and mixed-monitor DPI; move across monitors | Crop origin and dimensions remain correct in physical pixels, including negative desktop coordinates. |
| Target close/reopen and rapid target switching | No frames attributed to a reused/previous HWND; no lingering session or increasing resource usage. |
| Unsupported/failed capture while occluded | Clear unavailable state; no fallback image containing the foreground occluder. |
| Windows 19041 baseline and a current Windows 11 machine | Core HWND capture works within each tested environment; unsupported newer optional properties are not invoked. |
| HDR, protected content, RDP, exclusive fullscreen | Explicitly record observed behavior if required by supported use cases. Do not advertise support from the ordinary-window test results. |

The release decision should require passing the ordinary non-minimized occlusion cases and coordinate alignment on supported environments. A frozen or incompatible game renderer is a concrete compatibility result to expose to the user, not evidence that desktop cropping can recover the hidden pixels.
