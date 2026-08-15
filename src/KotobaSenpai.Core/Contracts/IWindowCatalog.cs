using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: enumerates the currently visible top-level windows.</summary>
public interface IWindowCatalog
{
    IReadOnlyList<WindowTarget> ListVisibleWindows();
}
