using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: shows an interactive region-selection overlay over a target window so the user can drag a recognition sub-region.</summary>
public interface IRegionSelector
{
    /// <summary>Shows the selector over the target window, initializing from <paramref name="initial"/> (or full window when null). On confirm, persists the region and hides.</summary>
    void Show(WindowTarget target, RecognitionRegion? initial = null);
}