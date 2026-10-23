
namespace KotobaSenpai.Core.TargetWindow;

/// <summary>Port: edits a recognition region and highlights it when recognition returns no text.</summary>
public interface IRegionSelector
{
    /// <summary>Shows the selector over the target window, initializing from <paramref name="initial"/> (or full window when null). On confirm, persists the region and hides.</summary>
    void Show(WindowTarget target, RecognitionRegion? initial = null);

    /// <summary>Flashes a passive red outline five times without changing the saved region or taking focus.</summary>
    void Flash(WindowTarget target, RecognitionRegion? region = null);

    /// <summary>Stops passive feedback without interrupting an active region edit.</summary>
    void StopFlashing();
}
