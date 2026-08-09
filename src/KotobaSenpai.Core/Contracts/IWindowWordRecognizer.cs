using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>一次识别的结果：捕获帧尺寸、按行（阅读顺序）排列的字符及可选告警。</summary>
public sealed record WordRecognitionResult(
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<OcrLine> Lines,
    string? Warning = null);

/// <summary>端口：对目标窗口执行日语 OCR，返回按行分组的字符级坐标。</summary>
public interface IWindowWordRecognizer
{
    Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default);
}