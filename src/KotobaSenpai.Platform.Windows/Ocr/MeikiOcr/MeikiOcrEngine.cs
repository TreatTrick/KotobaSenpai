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

/// <summary>识别出的单个字符及其在捕获帧中的像素框。</summary>
internal sealed record MeikiChar(char Char, int X1, int Y1, int X2, int Y2, float Conf);

/// <summary>识别出的一个文本行。</summary>
internal sealed record MeikiLine(string Text, IReadOnlyList<MeikiChar> Chars);

/// <summary>
/// meikiocr 本地 ONNX 引擎的 C# 移植。忠实复刻 rtr46/meikiocr 的 <c>meikiocr/ocr.py</c>
/// （Apache-2.0）：文本检测 → 横排识别 → 逐字符 NMS → 标点因子 → 交换错对修正。
/// 第一阶段仅处理横排文本（宽 &gt;= 高的框），竖排属后续变更。
/// 重心是复刻而非改进——任何改动都可能改变字符框坐标，破坏取词精度。
/// </summary>
public sealed class MeikiOcrEngine : IDisposable
{
    // --- 模型与输入尺寸（对应 ocr.py 常量） ---
    private const string DetModelName = "meiki.text.detect.v0.1.960x544.onnx";
    private const string RecModelName = "meiki.text.rec.v0.960x32.onnx";

    internal const int InputDetWidth = 960;
    internal const int InputDetHeight = 544;
    internal const int InputRecHeight = 32;
    internal const int InputRecWidth = 960;

    private const float XOverlapThreshold = 0.3f;
    private const float Epsilon = 1e-6f;

    // 对应 ocr.py 的 SWAPPED_PAIRS（模型把左右部件读反的常见汉字对）。
    private static readonly IReadOnlyDictionary<string, string> SwappedPairs = new Dictionary<string, string>
    {
        ["儡傀"] = "傀儡", ["談冗"] = "冗談", ["汰淘"] = "淘汰", ["沱滂"] = "滂沱",
        ["攣痙"] = "痙攣", ["酊酩"] = "酩酊", ["麭麺"] = "麺麭", ["哭慟"] = "慟哭",
    };

    // 双线性缩放（Triangle 核即 OpenCV 的 INTER_LINEAR）。
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

        // ponytail: 用 CPU 而非 DirectML —— 实测 DML 把检测模型置信度压到 ~0.18（CPU 为 ~0.72），
        // 导致全部低于 0.5 阈值、检测全灭。DirectML 对本模型精度不可用；若日后有可靠 GPU 路径再切回。
        _detSession = CreateSession(detPath);
        _recSession = CreateSession(recPath);
    }

    /// <summary>创建 CPU 推理会话。</summary>
    private static InferenceSession CreateSession(string modelPath)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        return new InferenceSession(modelPath, options);
    }

    /// <summary>对 Bgra32 捕获帧执行 meikiocr 管线，返回横排文本行。</summary>
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
            // 第一阶段只处理横排（宽 >= 高）；竖排框跳过。
            if (y2 - y1 > x2 - x1)
                continue;
            hIndices.Add(i);
        }
        _logger?.LogInformation("MeikiOcr: {h} horizontal, {v} vertical (skipped)", hIndices.Count, boxes.Count - hIndices.Count);

        ProcessRecognitionPipeline(image, boxes, hIndices, results, recThreshold, punctConfFactor);

        var lines = results.Where(r => r is not null).Cast<MeikiLine>().ToArray();
        _logger?.LogInformation("MeikiOcr: recognized {lines} lines, {chars} chars", lines.Length, lines.Sum(l => l.Chars.Count));
        // 诊断：打印每行实际识别出的文本，便于判断"认错字"还是"映射错位"。
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

    // --- 文本检测（对应 ocr.py run_detection） ---

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

        // 按 y（自上而下）排序，对应 ocr.py text_boxes.sort(key=bbox[1])。
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
                tensor[0, 0, y, x] = p.B / 255f; // B（OpenCV BGR 顺序）
                tensor[0, 1, y, x] = p.G / 255f; // G
                tensor[0, 2, y, x] = p.R / 255f; // R
            }
        }
        return (tensor, scale);
    }

    // --- 识别管线（对应 ocr.py _process_recognition_pipeline） ---

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

            // 横排：缩放到高 32、宽最长 960，再垫到 (960,32)。
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
        // char_codes 输出为 Int32（见 rec 模型输出元数据）。
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

                    // 横排映射（对应 ocr.py 非竖排分支）。
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

    // 对应 ocr.py _fix_swapped_pairs。
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
        // Bgra32 -> Rgba32（交换 B/R 通道）。
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
