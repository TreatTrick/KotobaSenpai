
namespace KotobaSenpai.Core.Overlay;

/// <summary>Port: shows/hides the underline overlay above the target window.</summary>
public interface IOverlayRenderer
{
    void Show(WordOverlaySession session);

    void Hide();
}
