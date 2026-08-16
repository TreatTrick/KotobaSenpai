using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>Resolves the region to pass to the frame capture for region-limited OCR: clamps it to the window and falls back to the full window for a too-small region. The physical screen-copy of just the region happens in the capture layer.</summary>
public static class CropRegion
{
    /// <summary>Returns the window-relative pixel rect to capture, or null (full window) when unset or too small to matter.</summary>
    public static ScreenRect? Resolve(ScreenRect? region, int windowWidth, int windowHeight)
    {
        if (region is not { } r || r.Width <= 0 || r.Height <= 0)
            return null;
        // ponytail: fixed 1/20 threshold — a sane floor for "this region is too small to matter"; tune if real crops misbehave.
        if (r.Width < windowWidth / 20 || r.Height < windowHeight / 20)
            return null;
        int x = Math.Clamp(r.X, 0, windowWidth);
        int y = Math.Clamp(r.Y, 0, windowHeight);
        int right = Math.Clamp(r.Right, x, windowWidth);
        int bottom = Math.Clamp(r.Bottom, y, windowHeight);
        return right == x || bottom == y ? null : new ScreenRect(x, y, right - x, bottom - y);
    }
}