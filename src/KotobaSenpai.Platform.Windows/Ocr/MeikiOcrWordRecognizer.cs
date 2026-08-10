using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;
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
    private const string OcrDiagEnabledKey = "OcrDiagEnabled";

    private readonly IWindowFrameCapture _capture;
    private readonly ISettingsService _settings;
    private readonly Lazy<MeikiOcrEngine> _engine;

    public MeikiOcrWordRecognizer(IWindowFrameCapture capture, ILogger logger, ISettingsService settings)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

        if (IsDiagEnabled())
            DumpDiagnostics(frame, lines, target);

        // 保留行结构：每行一个 OcrLine，供分词服务逐行重新组合成词。
        var ocrLines = lines
            .Select(meikiLine => new OcrLine(
                meikiLine.Chars
                    // 模型可能为空白/blank 兜底输出非词字符；OcrWord 拒绝空文本，须在构造前过滤。
                    .Where(charBox => !char.IsWhiteSpace(charBox.Char) && charBox.Char != '\0')
                    .Select(charBox => new CoreOcrWord(
                        charBox.Char.ToString(),
                        ToValidRect(charBox, frame.Width, frame.Height)))
                    .ToArray()))
            .Where(line => line.Words.Count > 0)
            .ToArray();

        return new WordRecognitionResult(frame.Width, frame.Height, ocrLines);
    }

    /// <summary>诊断落盘开关：设置项 <c>OcrDiagEnabled</c> 为 "true" 时开启（默认关）。</summary>
    private bool IsDiagEnabled()
        => string.Equals(_settings.GetValue(OcrDiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 诊断落盘：把捕获帧 PNG 与 OCR 识别文本存到固定目录，便于核对"截了什么、识别出什么"。
    /// </summary>
    private static void DumpDiagnostics(CapturedFrame frame, IReadOnlyList<MeikiLine> lines, WindowTarget target)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "diag");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("HHmmss-fff");
        SavePng(Path.Combine(dir, $"frame-{stamp}.png"), frame);
        var text = new List<string>
    {
        $"target={target.Title} bounds={target.Bounds}",
        $"frame={frame.Width}x{frame.Height} lines={lines.Count}",
    };
        text.AddRange(lines.Select(l => $"[{l.Text}] chars={l.Chars.Count}"));
        File.WriteAllLines(Path.Combine(dir, $"ocr-{stamp}.txt"), text);
    }

    private static void SavePng(string path, CapturedFrame frame)
    {
        // Bgra32 与 Format32bppArgb 在内存里同为 B,G,R,A 顺序，可直接拷贝。
        using var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(frame.Bgra32.ToArray(), 0, data.Scan0, frame.Bgra32.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        bmp.Save(path, ImageFormat.Png);
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