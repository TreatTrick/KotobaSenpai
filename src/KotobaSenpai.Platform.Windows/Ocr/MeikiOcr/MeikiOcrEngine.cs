using System.Globalization;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>A single recognized character and its pixel box in the captured frame.</summary>
internal sealed record MeikiChar(char Char, int X1, int Y1, int X2, int Y2, float Conf);

/// <summary>A single recognized text line.</summary>
internal sealed record MeikiLine(string Text, IReadOnlyList<MeikiChar> Chars);

/// <summary>
/// C# port of the meikiocr local ONNX engine. A faithful reproduction of rtr46/meikiocr's <c>meikiocr/ocr.py</c>
/// (Apache-2.0): text detection → horizontal recognition → per-character NMS → punctuation factor → swapped-pair fixes.
/// Phase one only handles horizontal text (boxes with width &gt;= height); vertical text is a later change. The focus is
/// fidelity, not improvement — any change may alter character-box coordinates and break word-picking accuracy.
/// </summary>
public sealed class MeikiOcrEngine : IDisposable
{
    // --- Model and input sizes (corresponding to ocr.py constants) ---
    private const string DetModelName = "meiki.text.detect.v0.1.960x544.onnx";
    private const string RecModelName = "meiki.text.rec.v0.960x32.onnx";

    internal const int InputDetWidth = 960;
    internal const int InputDetHeight = 544;
    internal const int InputRecHeight = 32;
    internal const int InputRecWidth = 960;

    private const float XOverlapThreshold = 0.3f;
    private const float Epsilon = 1e-6f;

    // Corresponds to SWAPPED_PAIRS in ocr.py (common kanji pairs the model reads with left/right parts swapped).
    private static readonly IReadOnlyDictionary<string, string> SwappedPairs = new Dictionary<string, string>
    {
        ["儡傀"] = "傀儡", ["談冗"] = "冗談", ["汰淘"] = "淘汰", ["沱滂"] = "滂沱",
        ["攣痙"] = "痙攣", ["酊酩"] = "酩酊", ["麭麺"] = "麺麭", ["哭慟"] = "慟哭",
    };

    // Bilinear resizing (the Triangle kernel is OpenCV's INTER_LINEAR).
    private static Image<Rgba32> ResizeBilinear(Image<Rgba32> image, int width, int height)
        => image.Clone(c => c.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Triangle,
            Size = new Size(width, height),
        }));

    private readonly InferenceSession _detSession;
    private readonly InferenceSession _recSession;
    private readonly int _maxBatchSize;
    private readonly ILogger? _logger;
    private bool _disposed;

    public MeikiOcrEngine(string modelDirectory, int maxBatchSize = 8, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        _maxBatchSize = maxBatchSize;
        _logger = logger;

        var detPath = Path.Combine(modelDirectory, DetModelName);
        var recPath = Path.Combine(modelDirectory, RecModelName);
        var missing = new[] { detPath, recPath }.Where(p => !File.Exists(p)).Select(Path.GetFileName);
        if (missing.Any())
            throw new WindowsPlatformException(
                ErrorCodes.OcrModelMissing,
                $"meikiocr model files not found in '{modelDirectory}'. Missing: {string.Join(", ", missing)}");

        // ponytail: CPU instead of DirectML — measured DML crushes detection-model confidence to ~0.18 (CPU ~0.72), dropping
        // everything below the 0.5 threshold and killing detection entirely. DirectML is unusable at this model's precision; switch back only if a reliable GPU path appears later.
        _detSession = CreateSession(detPath);
        _recSession = CreateSession(recPath);
    }

    /// <summary>Creates a CPU inference session.</summary>
    private static InferenceSession CreateSession(string modelPath)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        return new InferenceSession(modelPath, options);
    }

    /// <summary>Runs the meikiocr pipeline over a Bgra32 captured frame and returns horizontal text lines.</summary>
    internal IReadOnlyList<MeikiLine> RunOcr(
        ReadOnlyMemory<byte> bgra32,
        int width,
        int height,
        float detThreshold = 0.5f,
        float recThreshold = 0.1f,
        float punctConfFactor = 0.2f)
    {
        using var image = MakeRgbaImage(bgra32, width, height);
        var boxes = RunDetection(image, detThreshold);
        _logger?.LogInformation("MeikiOcr: frame {w}x{h}, detected {n} text boxes", width, height, boxes.Count);
        if (boxes.Count == 0)
            return Array.Empty<MeikiLine>();

        var results = new MeikiLine[boxes.Count];
        var hIndices = new List<int>();
        for (int i = 0; i < boxes.Count; i++)
        {
            var (x1, y1, x2, y2) = boxes[i];
            if (x2 - x1 <= 0 || y2 - y1 <= 0)
                continue;
            // Phase one only handles horizontal text (width >= height); vertical boxes are skipped.
            if (y2 - y1 > x2 - x1)
                continue;
            hIndices.Add(i);
        }
        _logger?.LogInformation("MeikiOcr: {h} horizontal, {v} vertical (skipped)", hIndices.Count, boxes.Count - hIndices.Count);

        ProcessRecognitionPipeline(image, boxes, hIndices, results, recThreshold, punctConfFactor);

        var lines = results.Where(r => r is not null).Cast<MeikiLine>().ToArray();
        _logger?.LogInformation("MeikiOcr: recognized {lines} lines, {chars} chars", lines.Length, lines.Sum(l => l.Chars.Count));
        // Diagnostics: print the text actually recognized on each line, to tell a misrecognized character from a mapping offset.
        foreach (var line in lines.Where(l => l.Text.Length > 0))
            _logger?.LogInformation("MeikiOcr: line '{text}' ({chars} chars)", line.Text, line.Chars.Count);
        return lines;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _detSession.Dispose();
        _recSession.Dispose();
        _disposed = true;
    }

    // --- Text detection (corresponding to run_detection in ocr.py) ---

    private List<(int X1, int Y1, int X2, int Y2)> RunDetection(Image<Rgba32> image, float confThreshold)
    {
        var (tensor, scale) = PreprocessForDetection(image);

        var inputNames = _detSession.InputNames.ToArray();
        var origSizes = new DenseTensor<long>(new[] { 1, 2 });
        origSizes[0, 0] = (long)(InputDetWidth / scale);
        origSizes[0, 1] = (long)(InputDetHeight / scale);
        using var outputs = _detSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputNames[0], tensor),
            NamedOnnxValue.CreateFromTensor(inputNames[1], origSizes),
        });

        var boxes = outputs[1].AsTensor<float>();
        var scores = outputs[2].AsTensor<float>();
        int count = boxes.Dimensions[1];

        var list = new List<(int X1, int Y1, int X2, int Y2)>();
        for (int j = 0; j < count; j++)
        {
            if (scores[0, j] <= confThreshold)
                continue;
            var x1 = Clamp(boxes[0, j, 0], 0, image.Width);
            var y1 = Clamp(boxes[0, j, 1], 0, image.Height);
            var x2 = Clamp(boxes[0, j, 2], 0, image.Width);
            var y2 = Clamp(boxes[0, j, 3], 0, image.Height);
            list.Add((x1, y1, x2, y2));
        }

        // Sort by y (top to bottom), corresponding to ocr.py text_boxes.sort(key=bbox[1]).
        list.Sort((a, b) => a.Y1.CompareTo(b.Y1));
        return list;
    }

    private static (DenseTensor<float> Tensor, float Scale) PreprocessForDetection(Image<Rgba32> image)
    {
        float scale = MathF.Min((float)InputDetWidth / image.Width, (float)InputDetHeight / image.Height);
        int wResized = (int)(image.Width * scale);
        int hResized = (int)(image.Height * scale);
        using var resized = ResizeBilinear(image, wResized, hResized);

        var tensor = new DenseTensor<float>(new[] { 1, 3, InputDetHeight, InputDetWidth });
        var pixels = new Rgba32[wResized * hResized];
        resized.CopyPixelDataTo(pixels);
        for (int y = 0; y < hResized; y++)
        {
            for (int x = 0; x < wResized; x++)
            {
                var p = pixels[y * wResized + x];
                tensor[0, 0, y, x] = p.B / 255f; // B (OpenCV BGR order)
                tensor[0, 1, y, x] = p.G / 255f; // G
                tensor[0, 2, y, x] = p.R / 255f; // R
            }
        }
        return (tensor, scale);
    }

    // --- Recognition pipeline (corresponding to _process_recognition_pipeline in ocr.py) ---

    private void ProcessRecognitionPipeline(
        Image<Rgba32> image,
        IReadOnlyList<(int X1, int Y1, int X2, int Y2)> boxes,
        IReadOnlyList<int> indices,
        MeikiLine?[] results,
        float recThreshold,
        float punctConfFactor)
    {
        var prep = PreprocessForRecognition(image, boxes, indices);
        if (prep is null)
            return;
        var (batch, validIndices, metadata) = prep.Value;

        var allLabels = new List<Tensor<int>>();
        var allBoxes = new List<Tensor<float>>();
        var allScores = new List<Tensor<float>>();
        for (int start = 0; start < batch.Length; start += _maxBatchSize)
        {
            int len = Math.Min(_maxBatchSize, batch.Length - start);
            var chunk = StackTensors(batch.AsSpan(start, len));
            (var labels, var boxesOut, var scores) = RunRecognitionInference(chunk);
            allLabels.Add(labels);
            allBoxes.Add(boxesOut);
            allScores.Add(scores);
        }

        PostprocessRecognitionResults(
            allLabels, allBoxes, allScores, validIndices, metadata,
            recThreshold, results, punctConfFactor);
    }

    private static (float[][] Tensors, List<int> ValidIndices, List<CropMeta> Metadata)?
        PreprocessForRecognition(
            Image<Rgba32> image,
            IReadOnlyList<(int X1, int Y1, int X2, int Y2)> boxes,
            IReadOnlyList<int> indices)
    {
        var tensors = new List<float[]>();
        var validIndices = new List<int>();
        var metadata = new List<CropMeta>();

        foreach (var i in indices)
        {
            var (x1, y1, x2, y2) = boxes[i];
            int w = x2 - x1, h = y2 - y1;
            if (w <= 0 || h <= 0)
                continue;

            // Horizontal: scale to height 32, width up to 960, then pad to (960,32).
            int newH = InputRecHeight;
            float scale = (float)newH / h;
            int newW = (int)Math.Round(w * scale);
            if (newW > InputRecWidth)
            {
                float scaleW = (float)InputRecWidth / newW;
                newW = InputRecWidth;
                newH = (int)Math.Round(newH * scaleW);
            }

            using var crop = image.Clone(c => c.Crop(new Rectangle(x1, y1, w, h)));
            using var resized = ResizeBilinear(crop, newW, newH);

            var tensor = new float[3 * InputRecHeight * InputRecWidth];
            var pixels = new Rgba32[newW * newH];
            resized.CopyPixelDataTo(pixels);
            for (int y = 0; y < newH; y++)
            {
                for (int x = 0; x < newW; x++)
                {
                    var p = pixels[y * newW + x];
                    tensor[y * InputRecWidth + x] = p.B / 255f;
                    tensor[InputRecHeight * InputRecWidth + y * InputRecWidth + x] = p.G / 255f;
                    tensor[2 * InputRecHeight * InputRecWidth + y * InputRecWidth + x] = p.R / 255f;
                }
            }

            tensors.Add(tensor);
            validIndices.Add(i);
            metadata.Add(new CropMeta(x1, y1, x2, y2, newW, newH));
        }

        return tensors.Count == 0 ? null : (tensors.ToArray(), validIndices, metadata);
    }

    private static DenseTensor<float> StackTensors(Span<float[]> tensors)
    {
        int b = tensors.Length;
        var result = new DenseTensor<float>(new[] { b, 3, InputRecHeight, InputRecWidth });
        var flat = result.Buffer.Span;
        for (int i = 0; i < b; i++)
            tensors[i].CopyTo(flat.Slice(i * 3 * InputRecHeight * InputRecWidth));
        return result;
    }

    private (Tensor<int> Labels, Tensor<float> Boxes, Tensor<float> Scores)
        RunRecognitionInference(DenseTensor<float> batch)
    {
        var inputNames = _recSession.InputNames.ToArray();
        var origSizes = new DenseTensor<long>(new[] { 1, 2 });
        origSizes[0, 0] = InputRecWidth;
        origSizes[0, 1] = InputRecHeight;
        using var outputs = _recSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputNames[0], batch),
            NamedOnnxValue.CreateFromTensor(inputNames[1], origSizes),
        });
        // char_codes output is Int32 (see the rec model's output metadata).
        var labels = outputs[0].AsTensor<int>();
        var boxes = outputs[1].AsTensor<float>();
        var scores = outputs[2].AsTensor<float>();
        return (
            new DenseTensor<int>(labels.ToArray(), labels.Dimensions.ToArray()),
            new DenseTensor<float>(boxes.ToArray(), boxes.Dimensions.ToArray()),
            new DenseTensor<float>(scores.ToArray(), scores.Dimensions.ToArray()));
    }

    private static void PostprocessRecognitionResults(
        IReadOnlyList<Tensor<int>> labelsChunks,
        IReadOnlyList<Tensor<float>> boxesChunks,
        IReadOnlyList<Tensor<float>> scoresChunks,
        IReadOnlyList<int> validIndices,
        IReadOnlyList<CropMeta> metadata,
        float recThreshold,
        MeikiLine?[] results,
        float punctConfFactor)
    {
        var candidatesByIndex = new Dictionary<int, List<Candidate>>();

        int chunkOffset = 0;
        for (int c = 0; c < labelsChunks.Count; c++)
        {
            var labels = labelsChunks[c];
            var boxes = boxesChunks[c];
            var scores = scoresChunks[c];
            int batchLen = labels.Dimensions[0];

            for (int i = 0; i < batchLen; i++)
            {
                int origIdx = validIndices[chunkOffset + i];
                var meta = metadata[chunkOffset + i];
                int cropW = meta.X2 - meta.X1, cropH = meta.Y2 - meta.Y1;
                int n = labels.Dimensions[1];

                if (!candidatesByIndex.TryGetValue(origIdx, out var list))
                {
                    list = new List<Candidate>();
                    candidatesByIndex[origIdx] = list;
                }

                for (int j = 0; j < n; j++)
                {
                    float score = scores[i, j];
                    if (score < recThreshold)
                        continue;

                    char ch = (char)labels[i, j];
                    float rx1 = boxes[i, j, 0], ry1 = boxes[i, j, 1], rx2 = boxes[i, j, 2], ry2 = boxes[i, j, 3];

                    // Horizontal mapping (corresponding to the non-vertical branch in ocr.py).
                    int effectiveW = meta.EffectiveW;
                    if (rx1 >= effectiveW)
                        continue;
                    rx1 = MathF.Min(rx1, effectiveW);
                    rx2 = MathF.Min(rx2, effectiveW);

                    float cx1 = rx1 / effectiveW * cropW;
                    float cx2 = rx2 / effectiveW * cropW;
                    float cy1 = ry1 / InputRecHeight * cropH;
                    float cy2 = ry2 / InputRecHeight * cropH;

                    int gx1c = meta.X1 + (int)cx1;
                    int gy1c = meta.Y1 + (int)cy1;
                    int gx2c = meta.X1 + (int)cx2;
                    int gy2c = meta.Y1 + (int)cy2;

                    list.Add(new Candidate(ch, gx1c, gy1c, gx2c, gy2c, score, (gx1c, gx2c)));
                }
            }
            chunkOffset += batchLen;
        }

        foreach (var (origIdx, candidates) in candidatesByIndex)
        {
            if (punctConfFactor != 1.0f)
                foreach (var cand in candidates)
                    if (IsPunctuation(cand.Char))
                        cand.Conf *= punctConfFactor;

            candidates.Sort((a, b) => b.Conf.CompareTo(a.Conf));

            var accepted = new List<Candidate>();
            var acceptedIntervals = new List<(int Start, int End)>();

            foreach (var cand in candidates)
            {
                int lenC = cand.Interval.End - cand.Interval.Start + (int)Epsilon;
                bool isOverlap = false;
                foreach (var (aStart, aEnd) in acceptedIntervals)
                {
                    if (cand.Interval.Start >= aEnd || aStart >= cand.Interval.End)
                        continue;
                    int interStart = Math.Max(cand.Interval.Start, aStart);
                    int interEnd = Math.Min(cand.Interval.End, aEnd);
                    int interLen = Math.Max(0, interEnd - interStart);
                    int lenA = aEnd - aStart + (int)Epsilon;
                    int minLen = Math.Min(lenC, lenA);
                    if (interLen / (float)minLen > XOverlapThreshold)
                    {
                        isOverlap = true;
                        break;
                    }
                }
                if (!isOverlap)
                {
                    accepted.Add(cand);
                    acceptedIntervals.Add(cand.Interval);
                }
            }

            accepted.Sort((a, b) => a.Interval.Start.CompareTo(b.Interval.Start));
            var chars = accepted.Select(c => c).ToList();
            var text = FixSwappedPairs(new string(chars.Select(c => c.Char).ToArray()), chars);
            results[origIdx] = new MeikiLine(text, chars.Select(c => new MeikiChar(c.Char, c.X1, c.Y1, c.X2, c.Y2, c.Conf)).ToArray());
        }
    }

    // Corresponds to _fix_swapped_pairs in ocr.py.
    private static string FixSwappedPairs(string text, List<Candidate> chars)
    {
        foreach (var (wrong, correct) in SwappedPairs)
        {
            int idx = text.IndexOf(wrong, StringComparison.Ordinal);
            if (idx != -1 && idx + 1 < chars.Count)
            {
                text = text[..idx] + correct + text[(idx + 2)..];
                (chars[idx], chars[idx + 1]) = (chars[idx + 1], chars[idx]);
            }
        }
        return text;
    }

    private static bool IsPunctuation(char c)
        => char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };

    private static int Clamp(float value, int min, int max)
        => (int)Math.Clamp(value, min, max);

    private static Image<Rgba32> MakeRgbaImage(ReadOnlyMemory<byte> bgra32, int width, int height)
    {
        // Bgra32 -> Rgba32 (swap the B/R channels).
        var rgba = new byte[bgra32.Length];
        var src = bgra32.Span;
        for (int i = 0; i < bgra32.Length; i += 4)
        {
            rgba[i + 0] = src[i + 2]; // R
            rgba[i + 1] = src[i + 1]; // G
            rgba[i + 2] = src[i + 0]; // B
            rgba[i + 3] = src[i + 3]; // A
        }
        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    private sealed record CropMeta(int X1, int Y1, int X2, int Y2, int EffectiveW, int EffectiveH);

    private sealed class Candidate
    {
        public Candidate(char ch, int x1, int y1, int x2, int y2, float conf, (int Start, int End) interval)
        {
            Char = ch;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Conf = conf;
            Interval = interval;
        }

        public char Char { get; set; }
        public int X1 { get; }
        public int Y1 { get; }
        public int X2 { get; }
        public int Y2 { get; }
        public float Conf { get; set; }
        public (int Start, int End) Interval { get; }
    }
}
