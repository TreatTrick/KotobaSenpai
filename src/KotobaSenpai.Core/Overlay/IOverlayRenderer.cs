
namespace KotobaSenpai.Core.Overlay;

/// <summary>Port: shows/hides the underline overlay above the target window.</summary>
public interface IOverlayRenderer
{
    /// <summary>Checks publication eligibility on the rendering thread immediately before displaying the session.</summary>
    void Show(WordOverlaySession session, Func<bool>? canRender = null);

    void Hide();
}
