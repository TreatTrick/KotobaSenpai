# Clutch support assets

Generated from the owner's `Clutch/Mascot/MascotA.heic` and `MascotB.heic`.
Both sequences have **121 frames, 249 × 560 pixels, 24 fps**. PNGs retain the
original transparent canvas and frame order; they are not resized website atlases.
`frames.json` records source hashes, dimensions and counts.

Regenerate from the repository root (development only):

```powershell
python -m pip install Pillow pillow-heif
python scripts/convert-support-assets.py D:/project/Clutch
```

`happy.png` is the last A frame; `thanks.png` is the first B frame. The three
payment images are unchanged copies of Clutch's original resources. Normal
builds and releases copy these generated files and require no HEIC decoder.
