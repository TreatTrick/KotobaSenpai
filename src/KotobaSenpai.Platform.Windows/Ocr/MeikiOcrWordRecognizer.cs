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
/// Runs Japanese OCR against the target window via the local meikiocr ONNX engine, producing character-level word boxes.
/// The model directory can be overridden with the <c>KOTOBA_MEIKIOCR_MODEL_DIR</c> environment variable
/// (development/testing); otherwise it falls back to Models/ under the app directory (shipped with the release). Throws
/// <see cref="WindowsPlatformException"/> (<see cref="ErrorCodes.OcrModelMissing"/>) when the model is missing.
/// </summary>
public sealed class MeikiOcrWordRecognizer : IWindowWordRecognizer
{
    private const string DiagEnabledKey = "DiagEnabled";

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

        // Preserve line structure: one OcrLine per line, so the tokenizer service can re-assemble words line by line.
        var ocrLines = lines
            .Select(meikiLine => new OcrLine(
                meikiLine.Chars
                    // The model may output non-word characters for blank/whitespace fallbacks; OcrWord rejects empty text, so filter before constructing.
                    .Where(charBox => !char.IsWhiteSpace(charBox.Char) && charBox.Char != '\0')
                    .Select(charBox => new CoreOcrWord(
                        charBox.Char.ToString(),
                        ToValidRect(charBox, frame.Width, frame.Height)))
                    .ToArray()))
            .Where(line => line.Words.Count > 0)
            .ToArray();

        return new WordRecognitionResult(frame.Width, frame.Height, ocrLines);
    }

    /// <summary>Toggle for writing diagnostics to disk: enabled when the setting <c>DiagEnabled</c> is "true" (off by default).</summary>
    private bool IsDiagEnabled()
        => string.Equals(_settings.GetValue(DiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes diagnostics to disk: saves the captured frame PNG and the OCR-recognized text to a fixed directory, so you can
    /// verify what was captured and what was recognized.
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
        // Bgra32 and Format32bppArgb are both B,G,R,A in memory, so a direct copy works.
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

    /// <summary>Clamps a character box into a valid <see cref="ScreenRect"/> (non-negative, in bounds, at least 1px).</summary>
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