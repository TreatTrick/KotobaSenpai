# KotobaSenpai app icon

## Design

The Japanese hiragana character **あ** inside a speech bubble represents understanding Japanese dialogue in place. A blue-to-teal rounded-square base, ivory foreground, soft highlights, and shallow relief follow the requested macOS app-icon aesthetic while keeping an original mark.

The artwork was generated with the built-in `image_gen` tool, followed by one AI cleanup pass. The PNG preserves the generated alpha transparency. No image-generation API key or extra runtime dependency is required.

## Assets and integration

- `app-icon.png`: the high-resolution AI-generated source, used by the sidebar.
- `app-icon.ico`: 32-bit RGBA PNG frames at 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 pixels.
- The project `ApplicationIcon` embeds the ICO in the Windows executable.
- Both assets are WPF resources; `MainWindow.Icon` and the custom title bar use the ICO.
- Existing taskbar pins may need to be recreated if Windows caches an older executable icon.

Regenerate the ICO after replacing the square PNG source, using Windows PowerShell:

```powershell
./tools/Export-AppIcon.ps1
```

The exporter only resizes and packages the AI artwork. It uses Windows' built-in System.Drawing and preserves alpha; the application does not run the exporter.

## Generation prompt

```text
Use case: logo-brand
Asset type: finished desktop application icon for KotobaSenpai, a Japanese reading companion that recognizes Japanese dialogue on screen and explains words in context.
Primary request: Create ONE original, beautifully simple app icon with the craft of Apple's macOS system app icons: an optically balanced rounded-square base, restrained dimensional materials, soft studio highlights, and a very strong small-size silhouette.
Subject: one large ivory-white speech bubble, with gently rounded corners and one short tail, containing ONLY the Japanese hiragana character "あ" in a bold, accurate, highly legible dark indigo Japanese typeface. The bubble is the single dominant object. This communicates understanding Japanese dialogue. Do not add a book, magnifier, mascot, AI sparkle or extra badges.
Background base: rich calm indigo-blue shading subtly into blue-teal, with a smooth satin-enamel finish and subtle raised rim. The white speech bubble has a very shallow porcelain relief and a soft short contact shadow, sophisticated and restrained, no glass glare obscuring the glyph.
Composition: perfectly front-on, centered, square canvas, one icon only; the rounded-square base occupies approximately 90 percent of the image width and height, leaving a narrow even TRANSPARENT outer margin. Bubble occupies approximately 65 percent of the base. The glyph is large, bold and centered inside the bubble. Use generous uncluttered negative space. Corners of the base are smoothly continuous like macOS icons. No tilted isometric view.
Lighting: soft upper-left light, smooth clean surfaces, crisp antialiased edges, minimal shadow confined near the icon.
Constraints: a production-ready icon asset, not a mockup, no scene, no desktop screenshot, no device, no presentation board, no multiple variants, no border frame outside the icon, no English lettering, no app name, no watermark, no Apple logo or copy of a particular existing icon. Only one exact character "あ". Genuine alpha transparency outside the rounded square, not a checkerboard painted into the image. Make the design read clearly at 16, 32 and 48 pixels. Square image, high resolution.
```

## Cleanup prompt

```text
Use case: precise-object-edit
Image 1 is the edit target: the app icon just generated for KotobaSenpai.
Make only this production cleanup: eliminate all stray blue/cyan pixels, wisps, fringe, scratches and detached speckles outside the smooth rounded-square silhouette. The exterior should be completely clean transparent alpha, with a smooth antialiased boundary. Also make the existing blue base finish slightly more restrained and satin rather than shiny plastic, keeping the same hues.
Preserve the overall composition, base proportions, smooth continuous rounded corners, ivory speech bubble silhouette and tail, large exact dark-blue hiragana "あ", positions, clear contrast, subtle relief, and excellent readability. Keep only that one Japanese character. No new elements. Keep front-on view, one centered icon only. Real transparency outside, no background, no checkerboard, no watermark. This is an application asset, not a presentation or mockup.
```
