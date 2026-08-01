using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>一次识别的结果：捕获帧尺寸、按阅读顺序排列的词及可选告警。</summary>
public sealed record WordRecognitionResult(
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<OcrWord> Words,
    string? Warning = null);

/// <summary>端口：对目标窗口执行日语 OCR，返回词级坐标。</summary>
public interface IWindowWordRecognizer
{
    Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default);
}
