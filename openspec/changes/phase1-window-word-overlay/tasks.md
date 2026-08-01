## 1. Solution and domain model

- [x] 1.1 Create the Core, Platform.Windows, App, and focused test projects targeting .NET 10 Windows.
- [x] 1.2 Implement immutable Core value objects for window identity, rectangles, OCR words, overlay lines, and session state with invariant validation.
- [x] 1.3 Define ports for window enumeration, frame capture, OCR, coordinate mapping, and overlay rendering.

## 2. Windows adapters

- [x] 2.1 Implement Win32 visible top-level window enumeration and in-memory target selection.
- [x] 2.2 Implement a single-frame Windows capture adapter with explicit OCR/capture errors (GDI compatibility path in phase 1).
- [x] 2.3 Implement Windows.Media.Ocr Japanese language detection and word-bound extraction.
- [x] 2.4 Implement physical-pixel coordinate transformation, clipping, and DPI-aware WPF conversion.

## 3. Overlay and application workflow

- [x] 3.1 Implement transparent, topmost, no-activate WPF overlay that renders one underline per valid word.
- [x] 3.2 Implement the capture-to-OCR-to-overlay application service and retry/error states.
- [x] 3.3 Build the WPF window picker and refresh/hide controls without persisting screenshots or raw window text.

## 4. Verification

- [x] 4.1 Add Core tests for invalid rectangles, empty OCR results, clipping, and session replacement.
- [x] 4.2 Add coordinate tests for 100%, 125%, and 150% DPI plus negative multi-monitor origins.
- [x] 4.3 Build and run all existing and new tests; manual smoke-test remains environment-dependent.
