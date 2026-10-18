using KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class OcrBitmapTests
{
    [Fact]
    public void Bitmap_owns_bgra_pixels_without_swapping_red_and_blue()
    {
        byte[] pixels = [1, 2, 3, 255, 40, 50, 60, 255, 70, 80, 90, 128, 100, 110, 120, 255];
        byte[] expected = [1, 2, 3, 255, 40, 50, 60, 255, 70, 80, 90, 128, 100, 110, 120, 255];
        using var bitmap = OcrBitmap.FromBgra(pixels, 2, 2);
        Array.Fill(pixels, (byte)0);

        var copy = OcrBitmap.CopyPixels(bitmap);

        Assert.Equal(expected, copy);
        Array.Fill(copy, (byte)0);
        Assert.Equal(expected, OcrBitmap.CopyPixels(bitmap));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 5)]
    public void Resizing_constant_color_keeps_edges_and_channels(int width, int height)
    {
        byte[] pixels = [30, 90, 180, 255, 30, 90, 180, 255, 30, 90, 180, 255, 30, 90, 180, 255];
        using var original = OcrBitmap.FromBgra(pixels, 2, 2);
        using var resized = OcrBitmap.Resize(original, width, height);

        var copy = OcrBitmap.CopyPixels(resized);

        Assert.Equal(width, resized.Width);
        Assert.Equal(height, resized.Height);
        Assert.Equal(width * height * 4, copy.Length);
        for (var i = 0; i < copy.Length; i += 4)
            Assert.Equal(new byte[] { 30, 90, 180, 255 }, copy.AsSpan(i, 4).ToArray());
    }

    [Fact]
    public void Bilinear_downscaling_combines_neighboring_pixels()
    {
        byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 255];
        using var original = OcrBitmap.FromBgra(pixels, 2, 2);
        using var resized = OcrBitmap.Resize(original, 1, 1);

        var copy = OcrBitmap.CopyPixels(resized);

        Assert.All(copy.Take(3), channel => Assert.InRange(channel, (byte)127, (byte)128));
        Assert.Equal(255, copy[3]);
    }

    [Fact]
    public void Invalid_bgra_buffer_size_is_rejected_before_native_copy()
    {
        Assert.Throws<ArgumentException>(() => OcrBitmap.FromBgra(new byte[4], 2, 2));
    }
}
