# KotobaSenpai app icon

## Design

The icon uses the supplied illustration of a purple-haired character above a white speech bubble containing the Japanese hiragana **あ**, set against a red rounded square. The black matte outside the rounded square has been made transparent while preserving the antialiased edge and the artwork's dark details.

## Assets and integration

- `app-icon.png`: the 1254 x 1254 RGBA source used by the sidebar and onboarding header.
- `app-icon.ico`: 32-bit RGBA PNG frames at 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 pixels.
- The project `ApplicationIcon` embeds the ICO in the Windows executable.
- Both assets are WPF resources; `MainWindow.Icon` and the custom title bar use the ICO.
- The installer uses the same ICO. Existing taskbar pins may need to be recreated if Windows caches an older executable icon.

Regenerate the ICO after replacing the PNG source, using Windows PowerShell:

```powershell
./tools/Export-AppIcon.ps1
```

The exporter only resizes and packages the artwork. It uses Windows' built-in System.Drawing and preserves alpha; the application does not run the exporter.
