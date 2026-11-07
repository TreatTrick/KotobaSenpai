using System.Drawing;
using KotobaSenpai.Core.Logging;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>
/// C# port of the meikiocr local ONNX engine. A faithful reproduction of rtr46/meikiocr's <c>meikiocr/ocr.py</c>
/// (Apache-2.0): text detection → horizontal recognition → per-character NMS → punctuation factor → swapped-pair fixes.
/// Phase one only handles horizontal text (boxes with width &gt;= height); vertical text is a later change. The focus is
/// fidelity, not improvement — any change may alter character-box coordinates and break word-picking accuracy.
/// <para>
/// This type is the orchestrator only: tensor preparation lives in <see cref="MeikiOcrPreprocessor"/>, model ownership
/// and inference in <see cref="MeikiOcrInferenceSession"/>, and decoding in <see cref="MeikiOcrResultDecoder"/>.
/// </para>
/// </summary>
public sealed class MeikiOcrEngine : IDisposable
{
    private readonly MeikiOcrInferenceSession _session;
    private readonly int _maxBatchSize;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private bool _disposed;

    public MeikiOcrEngine(string modelDirectory, int maxBatchSize = 8, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        _maxBatchSize = maxBatchSize;
        _logger = logger;
        _session = MeikiOcrInferenceSession.Open(modelDirectory);
    }

    /// <summary>Runs the meikiocr pipeline over a Bgra32 captured frame and returns horizontal text lines.</summary>
    internal IReadOnlyList<MeikiLine> RunOcr(
        ReadOnlyMemory<byte> bgra32,
        int width,
        int height,
        float detThreshold = 0.5f,
        float recThreshold = 0.1f,
        float punctConfFactor = 0.2f,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return OnnxRunCancellation.Execute(cancellationToken, options =>
            {
                using var image = OcrBitmap.FromBgra(bgra32, width, height);
                var boxes = _session.Detect(image, detThreshold, options, cancellationToken);
                _logger?.LogInformation("MeikiOcr: frame {0}x{1}, detected {2} text boxes", width, height, boxes.Count);
                if (boxes.Count == 0)
                    return Array.Empty<MeikiLine>();

                var horizontal = SelectHorizontalBoxes(boxes);
                _logger?.LogInformation("MeikiOcr: {0} horizontal, {1} vertical (skipped)", horizontal.Count, boxes.Count - horizontal.Count);

                var batch = MeikiOcrPreprocessor.CreateRecognitionBatch(image, boxes, horizontal, cancellationToken);
                if (batch is null)
                    return Array.Empty<MeikiLine>();

                var labels = new List<Tensor<int>>();
                var boxesOut = new List<Tensor<float>>();
                var scores = new List<Tensor<float>>();
                for (int start = 0; start < batch.Tensors.Length; start += _maxBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int length = Math.Min(_maxBatchSize, batch.Tensors.Length - start);
                    var chunk = MeikiOcrPreprocessor.StackTensors(batch.Tensors.AsSpan(start, length));
                    var (chunkLabels, chunkBoxes, chunkScores) = _session.Recognize(chunk, options);
                    labels.Add(chunkLabels);
                    boxesOut.Add(chunkBoxes);
                    scores.Add(chunkScores);
                }

                var lines = MeikiOcrResultDecoder.Decode(
                    labels, boxesOut, scores, batch.ValidIndices, batch.Metadata, boxes.Count,
                    recThreshold, punctConfFactor);
                _logger?.LogInformation("MeikiOcr: recognized {0} lines, {1} chars", lines.Count, lines.Sum(line => line.Chars.Count));
                // Diagnostics: print the text actually recognized on each line, to tell a misrecognized character from a mapping offset.
                foreach (var line in lines.Where(line => line.Text.Length > 0))
                    _logger?.LogInformation("MeikiOcr: line '{0}' ({1} chars)", line.Text, line.Chars.Count);
                cancellationToken.ThrowIfCancellationRequested();
                return lines;
            });
        }
    }

    /// <summary>Phase one only handles horizontal text (width &gt;= height); vertical boxes are skipped.</summary>
    private static List<int> SelectHorizontalBoxes(IReadOnlyList<MeikiBox> boxes)
    {
        var horizontal = new List<int>();
        for (int i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            if (box.X2 - box.X1 <= 0 || box.Y2 - box.Y1 <= 0)
                continue;
            if (box.Y2 - box.Y1 > box.X2 - box.X1)
                continue;
            horizontal.Add(i);
        }
        return horizontal;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _session.Dispose();
            _disposed = true;
        }
    }
}
