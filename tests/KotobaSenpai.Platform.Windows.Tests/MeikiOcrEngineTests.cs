using KotobaSenpai.Core.Localization;
using KotobaSenpai.Platform.Windows;
using KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;
using SixLabors.ImageSharp.PixelFormats;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>
/// meikiocr 引擎测试。缺模型测试始终运行（无需模型）；端到端黄金测试需
/// 本地具备模型文件（环境变量 <c>KOTOBA_MEIKIOCR_MODEL_DIR</c>）与样例图
/// （<c>KOTOBA_MEIKIOCR_GOLDEN_IMAGE</c>），否则跳过——模型不入 git，故 CI 不生效。
/// </summary>
public sealed class MeikiOcrEngineTests
{
    [Fact]
    public void Missing_model_directory_throws_ocr_model_missing()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "meiki-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var ex = Assert.Throws<WindowsPlatformException>(() => new MeikiOcrEngine(emptyDir));
            Assert.Equal(ErrorCodes.OcrModelMissing, ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void Missing_required_model_file_in_nonempty_dir_throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), "meiki-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "meiki.text.detect.v0.1.960x544.onnx"), "not-a-model");
            var ex = Assert.Throws<WindowsPlatformException>(() => new MeikiOcrEngine(dir));
            Assert.Equal(ErrorCodes.OcrModelMissing, ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Golden_end_to_end_skips_without_models_or_sample()
    {
        var modelDir = Environment.GetEnvironmentVariable("KOTOBA_MEIKIOCR_MODEL_DIR");
        var samplePath = Environment.GetEnvironmentVariable("KOTOBA_MEIKIOCR_GOLDEN_IMAGE");
        if (string.IsNullOrWhiteSpace(modelDir) || string.IsNullOrWhiteSpace(samplePath) || !File.Exists(samplePath))
            return; // 模型/样例未就绪，跳过（CI 无模型）。

        using var engine = new MeikiOcrEngine(modelDir);
        var (bgra, width, height) = LoadBgra(samplePath);
        var lines = engine.RunOcr(bgra, width, height);

        Assert.NotEmpty(lines);
        // 黄金校验：至少有一行非空文本，且字符框全部在帧内。
        foreach (var line in lines)
        {
            Assert.False(string.IsNullOrEmpty(line.Text));
            foreach (var c in line.Chars)
            {
                Assert.True(c.X2 > c.X1 && c.Y2 > c.Y1, "字符框必须非零面积");
                Assert.InRange(c.X1, 0, width);
                Assert.InRange(c.Y1, 0, height);
            }
        }
    }

    [Fact]
    public void Recognition_result_is_independent_of_batch_chunking()
    {
        var modelDir = Environment.GetEnvironmentVariable("KOTOBA_MEIKIOCR_MODEL_DIR");
        var samplePath = Environment.GetEnvironmentVariable("KOTOBA_MEIKIOCR_GOLDEN_IMAGE");
        if (string.IsNullOrWhiteSpace(modelDir) || string.IsNullOrWhiteSpace(samplePath) || !File.Exists(samplePath))
            return; // 模型/样例未就绪，跳过（CI 无模型）。

        var (bgra, width, height) = LoadBgra(samplePath);
        using var singleItemEngine = new MeikiOcrEngine(modelDir, maxBatchSize: 1);
        using var batchedEngine = new MeikiOcrEngine(modelDir, maxBatchSize: 8);

        var singleItemResult = singleItemEngine.RunOcr(bgra, width, height);
        var batchedResult = batchedEngine.RunOcr(bgra, width, height);

        Assert.Equal(Snapshot(singleItemResult), Snapshot(batchedResult));
    }

    private static string[] Snapshot(IReadOnlyList<MeikiLine> lines)
        => lines.Select(line => string.Join(
            '|',
            line.Text,
            string.Join(';', line.Chars.Select(c => $"{c.Char}:{c.X1},{c.Y1},{c.X2},{c.Y2}"))))
            .ToArray();

    private static (ReadOnlyMemory<byte> Bgra, int Width, int Height) LoadBgra(string path)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        var rgba = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rgba);
        var bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            bgra[i + 0] = rgba[i + 2]; // B
            bgra[i + 1] = rgba[i + 1]; // G
            bgra[i + 2] = rgba[i + 0]; // R
            bgra[i + 3] = rgba[i + 3]; // A
        }
        return (bgra, image.Width, image.Height);
    }
}
