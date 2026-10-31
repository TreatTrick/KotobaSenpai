using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KotobaSenpai.App.Controls;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.App.Tests;

public sealed class WindowPreviewPresentationTests
{
    [Fact]
    public void Shared_preview_pixels_are_small_and_do_not_retain_the_full_frame()
    {
        var frame = new CapturedFrame(800, 400, new byte[800 * 400 * 4]);
        var preview = WindowPreviewFlight.CreateThumbnailFrame(frame);
        Assert.Equal(640, preview.Width);
        Assert.Equal(320, preview.Height);
        Assert.Equal(640 * 320 * 4, preview.Bgra32.Length);
        Assert.NotEqual(frame.Bgra32, preview.Bgra32);
    }

    [Theory]
    [InlineData(16, 9, 144, 81)]
    [InlineData(9, 16, 81, 144)]
    public void Preview_fits_the_complete_image_and_preserves_negative_screen_origins(double width, double height, double expectedWidth, double expectedHeight)
    {
        var result = WindowPreviewFlight.Fit(new Rect(-300, 80, 144, 144), width / height);

        Assert.Equal(expectedWidth, result.Width, 3);
        Assert.Equal(expectedHeight, result.Height, 3);
        Assert.Equal(-300 + (144 - expectedWidth) / 2, result.X, 3);
        Assert.Equal(80 + (144 - expectedHeight) / 2, result.Y, 3);
    }

    [Fact]
    public void Static_thumbnail_is_small_frozen_and_independent_of_the_full_size_bitmap()
    {
        var pixels = Enumerable.Repeat((byte)255, 400 * 200 * 4).ToArray();
        var source = BitmapSource.Create(400, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 400 * 4);
        source.Freeze();

        var thumbnail = WindowPreviewFlight.CreateThumbnail(source, 144);

        Assert.Equal(144, thumbnail.PixelWidth);
        Assert.Equal(72, thumbnail.PixelHeight);
        Assert.True(thumbnail.IsFrozen);
        Assert.NotSame(source, thumbnail);
        Assert.IsNotType<TransformedBitmap>(thumbnail);
    }
}
