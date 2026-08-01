using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：枚举当前可见的顶层窗口。</summary>
public interface IWindowCatalog
{
    IReadOnlyList<WindowTarget> ListVisibleWindows();
}
