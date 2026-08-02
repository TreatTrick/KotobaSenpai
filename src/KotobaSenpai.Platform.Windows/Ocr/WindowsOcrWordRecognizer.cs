using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using CoreOcrWord = KotobaSenpai.Core.Models.OcrWord;

namespace KotobaSenpai.Platform.Windows.Ocr;

/// <summary>
/// 按需捕获目标窗口并调用系统日语 OCR。GDI 捕获作为 Windows.Graphics.Capture 的兼容回退。
/// 语言包缺失时不伪造坐标，抛出携带 <see cref="ErrorCodes.OcrLanguagePackMissing"/> 的
/// <see cref="WindowsPlatformException"/>；具体安装说明由表现层按码本地化，不在异常内嵌文案。
/// </summary>
public sealed class WindowsOcrWordRecognizer : IWindowWordRecognizer
{
    private readonly IWindowFrameCapture _capture;

    public WindowsOcrWordRecognizer(IWindowFrameCapture capture)
        => _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public async Task<WordRecognitionResult> RecognizeAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var language = new Language("ja-JP");
        var engine = OcrEngine.TryCreateFromLanguage(language);
        if (engine is null)
            throw new WindowsPlatformException(
                ErrorCodes.OcrLanguagePackMissing,
                "Japanese OCR language pack not found.");

        using var writer = new DataWriter();
        var frame = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false);
        writer.WriteBytes(frame.Bgra32.ToArray());
        IBuffer buffer = writer.DetachBuffer();
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        var words = result.Lines
            .SelectMany(line => line.Words)
            .Select(word =>
            {
                var rect = word.BoundingRect;
                var x = Math.Max(0, (int)Math.Floor(rect.X));
                var y = Math.Max(0, (int)Math.Floor(rect.Y));
                var width = Math.Max(1, (int)Math.Ceiling(rect.Width));
                var height = Math.Max(1, (int)Math.Ceiling(rect.Height));
                if (x >= frame.Width || y >= frame.Height)
                    return null;
                width = Math.Min(width, frame.Width - x);
                height = Math.Min(height, frame.Height - y);
                return new CoreOcrWord(word.Text, new ScreenRect(x, y, width, height));
            })
            .Where(word => word is not null)
            .Cast<CoreOcrWord>()
            .ToArray();

        return new WordRecognitionResult(frame.Width, frame.Height, words);
    }
}
