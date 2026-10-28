namespace KotobaSenpai.Core.TargetWindow;

/// <summary>Picks an existing desktop window; null means the user cancelled.</summary>
public interface IWindowPicker
{
    Task<WindowTarget?> PickAsync(CancellationToken cancellationToken = default);

    /// <summary>Follows the selected window with a passive outline. Repeated requests preserve its identity; null releases and resets the binding.</summary>
    void Highlight(WindowTarget? target);
}
