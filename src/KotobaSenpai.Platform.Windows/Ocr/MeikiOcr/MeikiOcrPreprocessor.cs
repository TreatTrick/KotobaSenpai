using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>
/// Tensor preparation for the meikiocr models, mirroring the preprocessing steps in <c>ocr.py</c>.
/// Detection letterboxes the whole frame into 960x544; recognition scales each detected line to height 32
/// with a width cap of 960 and zero padding. Both feed the models in BGR order with 0..1 floats.
/// </summary>
internal static class MeikiOcrPreprocessor
{
    internal const int InputDetWidth = 960;
    internal const int InputDetHeight = 544;
    internal const int InputRecHeight = 32;
    internal const int InputRecWidth = 960;

    /// <summary>Recognition inputs for one batch window plus the mapping back to detection indices.</summary>
    internal sealed record RecognitionBatch(float[][] Tensors, IReadOnlyList<int> ValidIndices, IReadOnlyList<CropMeta> Metadata);

    /// <summary>Letterboxes the frame and returns the detection tensor together with the applied scale.</summary>
    internal static (DenseTensor<float> Tensor, float Scale) CreateDetectionInput(Bitmap image, CancellationToken cancellationToken)
    {
        float scale = MathF.Min((float)InputDetWidth / image.Width, (float)InputDetHeight / image.Height);
        int wResized = (int)(image.Width * scale);
        int hResized = (int)(image.Height * scale);
        using var resized = OcrBitmap.Resize(image, wResized, hResized);

        var tensor = new DenseTensor<float>(new[] { 1, 3, InputDetHeight, InputDetWidth });
        var pixels = OcrBitmap.CopyPixels(resized);
        for (int y = 0; y < hResized; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < wResized; x++)
            {
                var p = (y * wResized + x) * 4;
                tensor[0, 0, y, x] = pixels[p] / 255f; // B (OpenCV BGR order)
                tensor[0, 1, y, x] = pixels[p + 1] / 255f; // G
                tensor[0, 2, y, x] = pixels[p + 2] / 255f; // R
            }
        }
        return (tensor, scale);
    }

    /// <summary>Builds one padded recognition tensor per usable box; returns null when nothing is usable.</summary>
    internal static RecognitionBatch? CreateRecognitionBatch(
        Bitmap image,
        IReadOnlyList<MeikiBox> boxes,
        IReadOnlyList<int> indices,
        CancellationToken cancellationToken)
    {
        var tensors = new List<float[]>();
        var validIndices = new List<int>();
        var metadata = new List<CropMeta>();

        foreach (var index in indices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var box = boxes[index];
            int width = box.X2 - box.X1, height = box.Y2 - box.Y1;
            if (width <= 0 || height <= 0)
                continue;

            // Horizontal: scale to height 32, width up to 960, then pad to (960,32).
            int newHeight = InputRecHeight;
            float scale = (float)newHeight / height;
            int newWidth = (int)Math.Round(width * scale);
            if (newWidth > InputRecWidth)
            {
                float scaleW = (float)InputRecWidth / newWidth;
                newWidth = InputRecWidth;
                newHeight = (int)Math.Round(newHeight * scaleW);
            }

            using var crop = image.Clone(new Rectangle(box.X1, box.Y1, width, height), PixelFormat.Format32bppArgb);
            using var resized = OcrBitmap.Resize(crop, newWidth, newHeight);

            var tensor = new float[3 * InputRecHeight * InputRecWidth];
            var pixels = OcrBitmap.CopyPixels(resized);
            for (int y = 0; y < newHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < newWidth; x++)
                {
                    var p = (y * newWidth + x) * 4;
                    tensor[y * InputRecWidth + x] = pixels[p] / 255f;
                    tensor[InputRecHeight * InputRecWidth + y * InputRecWidth + x] = pixels[p + 1] / 255f;
                    tensor[2 * InputRecHeight * InputRecWidth + y * InputRecWidth + x] = pixels[p + 2] / 255f;
                }
            }

            tensors.Add(tensor);
            validIndices.Add(index);
            metadata.Add(new CropMeta(box.X1, box.Y1, box.X2, box.Y2, newWidth, newHeight));
        }

        return tensors.Count == 0 ? null : new RecognitionBatch(tensors.ToArray(), validIndices, metadata);
    }

    /// <summary>Stacks per-line tensors into one batch tensor.</summary>
    internal static DenseTensor<float> StackTensors(ReadOnlySpan<float[]> tensors)
    {
        int batch = tensors.Length;
        var result = new DenseTensor<float>(new[] { batch, 3, InputRecHeight, InputRecWidth });
        var flat = result.Buffer.Span;
        for (int i = 0; i < batch; i++)
            tensors[i].CopyTo(flat.Slice(i * 3 * InputRecHeight * InputRecWidth));
        return result;
    }
}
