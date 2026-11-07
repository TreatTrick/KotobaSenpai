using System.Text;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;
using static KotobaSenpai.Core.TargetWindow.WindowOcclusionGeometry;
using static KotobaSenpai.Platform.Windows.Overlay.WindowNativeMethods;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Collects native occluders and reports changes in their coverage diagnostics.</summary>
internal sealed class WindowOcclusionEvaluator(ILogger? logger)
{
    private const long WsExLayered = 0x00080000;
    private readonly ILogger? _logger = logger;
    private string? _lastDiagnosticState;

    public void Reset() => _lastDiagnosticState = null;

    public bool IsOccluded(ScreenRect targetBounds, nint targetHandle)
    {
        var auxiliaryWindowHandles = OverlayWindowRegistry.Snapshot();
        var occluders = new List<ScreenRect>();
        var candidates = new List<(nint Handle, ScreenRect Bounds, int ExtendedStyle, string Exclusion)>();
        for (var above = WindowNativeMethods.GetWindow(targetHandle, GwHwndPrev);
             above != nint.Zero;
             above = WindowNativeMethods.GetWindow(above, GwHwndPrev))
        {
            if (!WindowNativeMethods.IsWindowVisible(above)
                || WindowNativeMethods.IsIconic(above)
                || !WindowNativeMethods.GetWindowRect(above, out var rect)
                || rect.Right <= rect.Left
                || rect.Bottom <= rect.Top)
                continue;

            var occluderBounds = new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (!HasPositiveAreaIntersection(targetBounds, occluderBounds))
                continue;
            var extendedStyle = WindowNativeMethods.GetWindowLong(above, GwlExStyle);
            var exclusion = auxiliaryWindowHandles.Contains(above) ? "auxiliary"
                : !CanOcclude(extendedStyle) ? "layered-click-through" : "none";
            candidates.Add((above, occluderBounds, extendedStyle, exclusion));
            if (exclusion == "none")
                occluders.Add(occluderBounds);
        }

        var occluded = ExceedsOcclusionThreshold(targetBounds, occluders, out var occlusionRatio);
        LogOcclusionEvaluation(targetHandle, targetBounds, candidates, occluders.Count, occlusionRatio, occluded);
        return occluded;
    }

    private void LogOcclusionEvaluation(nint targetHandle, ScreenRect targetBounds,
        IReadOnlyList<(nint Handle, ScreenRect Bounds, int ExtendedStyle, string Exclusion)> candidates,
        int occluderCount, double ratio, bool occluded)
    {
        if (_logger is null)
            return;
        var state = $"{targetBounds}|{string.Join(";", candidates.Select(window => $"{window.Handle}:{window.Bounds}:{window.ExtendedStyle:X}:{window.Exclusion}"))}";
        if (state == _lastDiagnosticState)
            return;
        _lastDiagnosticState = state;
        _logger.Log(LogLevel.Trace, null, "[OVERLAY] Occlusion evaluated: targetHandle={0}, targetBounds={1}, occlusionRatio={2:R}, hideAboveRatio={3:R}, occluded={4}, occluderCount={5}, intersectingWindowCount={6}.",
            targetHandle, targetBounds, ratio, OcclusionHideThreshold, occluded, occluderCount, candidates.Count);
        foreach (var window in candidates)
        {
            var className = new StringBuilder(256);
            WindowNativeMethods.GetClassName(window.Handle, className, className.Capacity);
            WindowNativeMethods.GetWindowThreadProcessId(window.Handle, out var processId);
            var cloakResult = WindowNativeMethods.DwmGetWindowAttribute(window.Handle, 14, out var cloaked, sizeof(uint));
            _logger.Log(LogLevel.Trace, null, "[OVERLAY] Occlusion candidate: hwnd={0}, processId={1}, className={2}, exStyle=0x{3:X}, bounds={4}, clientIntersection={5}, exclusion={6}, cloaked={7}.",
                window.Handle, processId, className.ToString(), window.ExtendedStyle, window.Bounds,
                window.Bounds.ClampTo(targetBounds), window.Exclusion, cloakResult >= 0 ? cloaked.ToString() : "unknown");
        }
    }

    internal static bool CanOcclude(long extendedStyle)
    {
        // A layered click-through helper can span transparent desktop areas; its outer rectangle is not a solid occluder.
        const long clickThroughLayered = WsExLayered | WsExTransparent;
        return (extendedStyle & clickThroughLayered) != clickThroughLayered;
    }
}
