# Remove ImageSharp Implementation Plan

**Goal:** Remove the vulnerable ImageSharp dependency and the temporary local audit-warning override without requiring a new build-time license.

**Approved approach:** The user selected Windows-native image processing after comparing an ImageSharp upgrade with removing the dependency. Reuse `System.Drawing`, already supplied by this Windows desktop project. Keep WGC capture, BGR/CHW tensor construction, crop coordinates and ONNX inference contracts unchanged.

**Tech stack:** .NET 10 Windows Desktop, System.Drawing/GDI+, existing ONNX Runtime and xUnit.

## 1. Record a reference before replacing the backend

- Run the existing ImageSharp 3.1.12 component against three saved diagnostic PNGs, using the checked-in model files available locally.
- Save recognized text and character rectangles under ignored `bin/ocr-compatibility-probe/`.
- This is an offline check; do not capture pixels, launch windows or manipulate the user's desktop.

## 2. Replace in-memory image operations

- Add `src/KotobaSenpai.Platform.Windows/Ocr/MeikiOcr/OcrBitmap.cs` for owned BGRA bitmaps, native bilinear resizing and tightly packed BGRA readback.
- Add focused tests in `tests/KotobaSenpai.Platform.Windows.Tests/OcrBitmapTests.cs` for channel order, memory ownership, constant-color borders, downscaling and invalid buffer sizes; observe failures before implementation.
- Use high-quality bilinear interpolation with half-pixel sampling and mirrored edge handling. Dispose graphics, image attributes, bitmap locks and temporary images promptly.
- Update `MeikiOcrEngine.cs` to use these operations while retaining existing crop dimensions and BGR tensor normalization.
- Update the golden-test PNG loader to use System.Drawing.
- Remove the package reference and the ImageSharp third-party notice. Keep historical archived design decisions intact.

## 3. Verify the replacement

- Compare offline OCR text and character geometry with the recorded reference, and record material differences.
- Run `dotnet restore KotobaSenpai.slnx --force-evaluate --disable-parallel`, `dotnet test KotobaSenpai.slnx`, and Debug/Release builds with normal auditing and warnings-as-errors.
- Run `dotnet list KotobaSenpai.slnx package --vulnerable --include-transitive` and check that no solution project resolves ImageSharp.
- Build the actual App Debug output when its files are available, then verify the runtime dependency manifest and copied DLLs. Actual live-window acceptance remains with the user.

## Completed verification

- Normal audited restore and both App Debug/Release rebuilds passed with zero warnings/errors; no audit bypass or license key is configured.
- All 393 tests passed with local models and the saved two-line sample supplied, exercising actual inference and batch-chunking consistency in addition to five new bitmap cases.
- The full solution audit, including transitive dependencies, reported no known vulnerable packages. The rebuilt App Debug directory and dependency manifest contain no ImageSharp/Six Labors assembly or package; its Windows component matches the project build by SHA-256.
- The offline comparison used the original ImageSharp 3.1.12 component and three previously saved diagnostic frames. No screenshots were captured, windows launched, or desktop settings changed. Comparison artifacts remain local under ignored `bin/ocr-compatibility-probe/`.

| Saved diagnostic frame | Dimensions | Characters before/after | Text substitutions | Maximum coordinate delta |
| --- | --- | --- | --- | --- |
| `7e75b6c1a38a46e7add2f3f65ea937e7` | 1235 × 186 | 32 / 32 | One punctuation mark: `、` → `「` | 1 physical pixel |
| `aae2b8901f574409abdde72c9f4d15c4` | 1235 × 186 | 28 / 28 | None | 1 physical pixel |
| `ed267eec07d34b61ba289232054e9da3` | 1979 × 298 | 45 / 45 | None | 1 physical pixel |

Native and ImageSharp resampling are not bit-identical. These three cases preserve line/character counts and bound observed character-coordinate differences to one pixel; they do not establish identical recognition for every image. Actual live-window acceptance remains with the user.
