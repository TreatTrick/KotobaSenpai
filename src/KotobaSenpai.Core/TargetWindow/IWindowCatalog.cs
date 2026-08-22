
namespace KotobaSenpai.Core.TargetWindow;

/// <summary>Port: enumerates the currently visible top-level windows.</summary>
public interface IWindowCatalog
{
    IReadOnlyList<WindowTarget> ListVisibleWindows();
}
