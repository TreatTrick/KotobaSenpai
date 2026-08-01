using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：捕获目标窗口的单帧画面。像素数据仅在进程内传递，不持久化。</summary>
public interface IWindowFrameCapture
{
    Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default);
}
