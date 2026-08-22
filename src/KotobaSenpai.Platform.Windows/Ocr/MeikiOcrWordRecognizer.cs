using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;
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
    private readonly MeikiOcrEngine _engine;

    public MeikiOcrWordRecognizer(IWindowFrameCapture capture, ILogger logger, ISettingsService settings)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _engine = new MeikiOcrEngine(ResolveModelDirectory(), logger: logger);
    }

    public async Task<WordRecognitionResult> RecognizeAsync(
        Guid recognitionId,
        WindowTarget target,
        CancellationToken cancellationToken = default,
        ScreenRect? region = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int frameWidth = target.Bounds.Width, frameHeight = target.Bounds.Height;
        // The capture layer grabs only the region's screen rectangle directly (no whole-window grab then crop). A
        // degenerate region falls back to the full window. Boxes from a region OCR are offset back to full-frame coords.
        var crop = CropRegion.Resolve(region, frameWidth, frameHeight);
        var frame = await _capture.CaptureAsync(target, cancellationToken, crop).ConfigureAwait(false);
        var offsetX = crop?.X ?? 0;
        var offsetY = crop?.Y ?? 0;

        IReadOnlyList<MeikiLine> lines;
        try
        {
            lines = _engine.RunOcr(frame.Bgra32, frame.Width, frame.Height);
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
            DumpDiagnostics(recognitionId, frame, lines, target);

        // Preserve line structure: one OcrLine per line, so the tokenizer service can re-assemble words line by line.
        // Boxes from a region OCR are offset back to full-frame coordinates by the crop origin.
        var ocrLines = lines
            .Select(meikiLine => new OcrLine(
                meikiLine.Chars
                    // The model may output non-word characters for blank/whitespace fallbacks; OcrWord rejects empty text, so filter before constructing.
                    .Where(charBox => !char.IsWhiteSpace(charBox.Char) && charBox.Char != '\0')
                    .Select(charBox => new CoreOcrWord(
                        charBox.Char.ToString(),
                        ToValidRect(charBox, frameWidth, frameHeight, offsetX, offsetY)))
                    .ToArray()))
            .Where(line => line.Words.Count > 0)
            .ToArray();

        // Report the full-window frame dimensions (the capture may have been region-sized) so downstream screen mapping stays correct.
        return new WordRecognitionResult(frameWidth, frameHeight, ocrLines);
    }

    /// <summary>Toggle for writing diagnostics to disk: enabled when the setting <c>DiagEnabled</c> is "true" (off by default).</summary>
    private bool IsDiagEnabled()
        => string.Equals(_settings.GetValue(DiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes diagnostics to disk: saves the captured frame PNG and the OCR-recognized text to a fixed directory, so you can
    /// verify what was captured and what was recognized.
    /// </summary>
    internal static void DumpDiagnostics(
        Guid recognitionId,
        CapturedFrame frame,
        IReadOnlyList<MeikiLine> lines,
        WindowTarget target,
        string? diagnosticDirectory = null)
    {
        var dir = diagnosticDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "diag");
        Directory.CreateDirectory(dir);
        var runId = recognitionId.ToString("N");
        SavePng(Path.Combine(dir, $"frame-{runId}.png"), frame);
        var text = new List<string>
    {
        $"target={target.Title} bounds={target.Bounds}",
        $"frame={frame.Width}x{frame.Height} lines={lines.Count}",
        };
        text.AddRange(lines.Select(l => $"[{l.Text}] chars={l.Chars.Count}"));
        File.WriteAllLines(Path.Combine(dir, $"ocr-{runId}.txt"), text);
        PruneToLatest(dir, "frame-");
        PruneToLatest(dir, "ocr-");
    }

    /// <summary>Keeps only the latest <paramref name="max"/> files whose name starts with <paramref name="prefix"/>, deleting older ones so diag never accumulates unboundedly.</summary>
    private static void PruneToLatest(string dir, string prefix, int max = 10)
    {
        foreach (var file in Directory.GetFiles(dir, $"{prefix}*")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(max))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
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

    /// <summary>Clamps a character box into a valid <see cref="ScreenRect"/>, offset by the crop origin so cropped-OCR boxes land in full-frame coordinates.</summary>
    private static ScreenRect ToValidRect(MeikiChar c, int frameWidth, int frameHeight, int offsetX = 0, int offsetY = 0)
    {
        var x1 = Math.Max(0, c.X1 + offsetX);
        var y1 = Math.Max(0, c.Y1 + offsetY);
        var x2 = Math.Max(x1 + 1, Math.Min(frameWidth, c.X2 + offsetX));
        var y2 = Math.Max(y1 + 1, Math.Min(frameHeight, c.Y2 + offsetY));
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
