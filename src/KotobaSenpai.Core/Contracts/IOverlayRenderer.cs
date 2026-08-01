using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：在目标窗口上方显示/隐藏下划线覆盖层。</summary>
public interface IOverlayRenderer
{
    void Show(WordOverlaySession session);

    void Hide();
}
