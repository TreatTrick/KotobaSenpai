using System.Drawing;
using System.IO;
using KotobaSenpai.Core.Errors;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>
/// Owns the two ONNX inference sessions and turns model outputs into detection boxes and recognition tensors.
/// This type is the only place that knows the model file names and their input/output contract.
/// </summary>
internal sealed class MeikiOcrInferenceSession : IDisposable
{
    private const string DetModelName = "meiki.text.detect.v0.1.960x544.onnx";
    private const string RecModelName = "meiki.text.rec.v0.960x32.onnx";

    private readonly InferenceSession _detSession;
    private readonly InferenceSession _recSession;

    private MeikiOcrInferenceSession(InferenceSession detSession, InferenceSession recSession)
    {
        _detSession = detSession;
        _recSession = recSession;
    }

    /// <summary>Validates the model directory and opens both sessions.</summary>
    /// <exception cref="WindowsPlatformException">A required model file is missing (<c>OcrModelMissing</c>).</exception>
    public static MeikiOcrInferenceSession Open(string modelDirectory)
    {
        var detPath = Path.Combine(modelDirectory, DetModelName);
        var recPath = Path.Combine(modelDirectory, RecModelName);
        var missing = new[] { detPath, recPath }.Where(path => !File.Exists(path)).Select(Path.GetFileName);
        if (missing.Any())
            throw new WindowsPlatformException(
                ErrorCodes.OcrModelMissing,
                $"meikiocr model files not found in '{modelDirectory}'. Missing: {string.Join(", ", missing)}");

        // ponytail: CPU instead of DirectML — measured DML crushes detection-model confidence to ~0.18 (CPU ~0.72), dropping
        // everything below the 0.5 threshold and killing detection entirely. DirectML is unusable at this model's precision; switch back only if a reliable GPU path appears later.
        var detSession = CreateSession(detPath);
        try
        {
            return new MeikiOcrInferenceSession(detSession, CreateSession(recPath));
        }
        catch
        {
            // Do not leak the detection session when the recognition model fails to open.
            detSession.Dispose();
            throw;
        }
    }

    /// <summary>Creates a CPU inference session.</summary>
    private static InferenceSession CreateSession(string modelPath)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        return new InferenceSession(modelPath, options);
    }

    /// <summary>Runs text detection and returns boxes above the threshold, sorted top to bottom.</summary>
    public IReadOnlyList<MeikiBox> Detect(
        Bitmap image,
        float confidenceThreshold,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        var (tensor, scale) = MeikiOcrPreprocessor.CreateDetectionInput(image, cancellationToken);

        var inputNames = _detSession.InputNames.ToArray();
        var origSizes = new DenseTensor<long>(new[] { 1, 2 });
        origSizes[0, 0] = (long)(MeikiOcrPreprocessor.InputDetWidth / scale);
        origSizes[0, 1] = (long)(MeikiOcrPreprocessor.InputDetHeight / scale);
        using var outputs = _detSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputNames[0], tensor),
            NamedOnnxValue.CreateFromTensor(inputNames[1], origSizes),
        }, _detSession.OutputNames, options);

        var boxes = outputs[1].AsTensor<float>();
        var scores = outputs[2].AsTensor<float>();
        int count = boxes.Dimensions[1];

        var detected = new List<MeikiBox>();
        for (int j = 0; j < count; j++)
        {
            if (scores[0, j] <= confidenceThreshold)
                continue;
            detected.Add(new MeikiBox(
                Clamp(boxes[0, j, 0], image.Width),
                Clamp(boxes[0, j, 1], image.Height),
                Clamp(boxes[0, j, 2], image.Width),
                Clamp(boxes[0, j, 3], image.Height)));
        }

        // Sort by y (top to bottom), corresponding to ocr.py text_boxes.sort(key=bbox[1]).
        detected.Sort((a, b) => a.Y1.CompareTo(b.Y1));
        return detected;
    }

    /// <summary>Runs one recognition batch and returns per-character codes, boxes and scores.</summary>
    public (Tensor<int> Labels, Tensor<float> Boxes, Tensor<float> Scores) Recognize(
        DenseTensor<float> batch,
        RunOptions options)
    {
        var inputNames = _recSession.InputNames.ToArray();
        var origSizes = new DenseTensor<long>(new[] { 1, 2 });
        origSizes[0, 0] = MeikiOcrPreprocessor.InputRecWidth;
        origSizes[0, 1] = MeikiOcrPreprocessor.InputRecHeight;
        using var outputs = _recSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputNames[0], batch),
            NamedOnnxValue.CreateFromTensor(inputNames[1], origSizes),
        }, _recSession.OutputNames, options);
        // char_codes output is Int32 (see the rec model's output metadata).
        var labels = outputs[0].AsTensor<int>();
        var boxes = outputs[1].AsTensor<float>();
        var scores = outputs[2].AsTensor<float>();
        return (
            new DenseTensor<int>(labels.ToArray(), labels.Dimensions.ToArray()),
            new DenseTensor<float>(boxes.ToArray(), boxes.Dimensions.ToArray()),
            new DenseTensor<float>(scores.ToArray(), scores.Dimensions.ToArray()));
    }

    public void Dispose()
    {
        _detSession.Dispose();
        _recSession.Dispose();
    }

    private static int Clamp(float value, int max)
        => (int)Math.Clamp(value, 0, max);
}
