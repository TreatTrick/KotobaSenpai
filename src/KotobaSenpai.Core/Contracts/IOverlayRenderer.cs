using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: shows/hides the underline overlay above the target window.</summary>
public interface IOverlayRenderer
{
    void Show(WordOverlaySession session);

    void Hide();
}
