using System.IO;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;
using CoreOcrWord = KotobaSenpai.Core.Models.OcrWord;

namespace KotobaSenpai.Platform.Windows.Ocr;

/// <summary>
/// 通过本地 meikiocr ONNX 引擎对目标窗口执行日语 OCR，输出字符级词框。
/// 模型目录可用环境变量 <c>KOTOBA_MEIKIOCR_MODEL_DIR</c> 覆盖（开发/测试）；
/// 否则回退到程序目录下的 Models/（随发布打包）。模型缺失时抛
/// <see cref="WindowsPlatformException"/>（<see cref="ErrorCodes.OcrModelMissing"/>）。
/// </summary>
public sealed class MeikiOcrWordRecognizer : IWindowWordRecognizer
{
    private readonly IWindowFrameCapture _capture;
    private readonly Lazy<MeikiOcrEngine> _engine;

    public MeikiOcrWordRecognizer(IWindowFrameCapture capture, ILogger logger)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _engine = new Lazy<MeikiOcrEngine>(
            () => new MeikiOcrEngine(ResolveModelDirectory(), logger: logger),
            isThreadSafe: true);
    }

    public async Task<WordRecognitionResult> RecognizeAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var frame = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MeikiLine> lines;
        try
        {
            lines = _engine.Value.RunOcr(frame.Bgra32, frame.Width, frame.Height);
        }
        catch (WindowsPlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WindowsPlatformException(
                ErrorCodes.OcrInferenceFailed,
                "meikiocr inference failed.",
                ex);
        }

        var words = lines
            .SelectMany(line => line.Chars)
            // 模型可能为空白/blank 兜底输出非词字符；OcrWord 拒绝空文本，须在构造前过滤。
            .Where(charBox => !char.IsWhiteSpace(charBox.Char) && charBox.Char != '\0')
            .Select(charBox => new CoreOcrWord(
                charBox.Char.ToString(),
                ToValidRect(charBox, frame.Width, frame.Height)))
            .ToArray();

        return new WordRecognitionResult(frame.Width, frame.Height, words);
    }

    /// <summary>把字符框钳制为合法 <see cref="ScreenRect"/>（非负、不越界、至少 1px）。</summary>
    private static ScreenRect ToValidRect(MeikiChar c, int frameWidth, int frameHeight)
    {
        var x1 = Math.Max(0, c.X1);
        var y1 = Math.Max(0, c.Y1);
        var x2 = Math.Max(x1 + 1, Math.Min(frameWidth, c.X2));
        var y2 = Math.Max(y1 + 1, Math.Min(frameHeight, c.Y2));
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static string ResolveModelDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KOTOBA_MEIKIOCR_MODEL_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.GetFullPath(overrideDir);
        return Path.Combine(AppContext.BaseDirectory, "Models");
    }
}