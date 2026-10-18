using System.Runtime.InteropServices;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Platform.Windows.Tests.Capture;

public sealed class WgcFrameLayoutTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    public void Client_and_region_offsets_use_physical_pixels_at_any_dpi(int scale)
    {
        var window = new ScreenRect(-2 * scale, -scale, 5 * scale, 3 * scale);
        var client = new ScreenRect(window.X + scale / 10, window.Y + scale / 2, 4 * scale, 2 * scale);
        var region = new ScreenRect(scale, scale / 2, scale, scale);

        var crop = WgcFrameLayout.ResolveCrop(window, client, window.Width, window.Height, region);

        Assert.Equal(new ScreenRect(scale / 10 + scale, 2 * (scale / 2), scale, scale), crop);
    }

    [Fact]
    public void Mismatched_content_size_is_rejected_instead_of_scaled()
    {
        var window = new ScreenRect(0, 0, 500, 300);
        var client = new ScreenRect(1, 30, 498, 269);

        Assert.Null(WgcFrameLayout.ResolveCrop(window, client, 400, 200, null));
        Assert.Null(WgcFrameLayout.ResolveCrop(window, new ScreenRect(1, 30, 600, 269), 500, 300, null));
    }

    [Fact]
    public void Full_client_crop_excludes_decoration_and_rejects_regions_outside_the_client()
    {
        var window = new ScreenRect(-200, 10, 500, 300);
        var client = new ScreenRect(-199, 40, 498, 269);
        Assert.Equal(new ScreenRect(1, 30, 498, 269), WgcFrameLayout.ResolveCrop(window, client, 500, 300, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WgcFrameLayout.ResolveCrop(window, client, 500, 300, new ScreenRect(490, 0, 10, 20)));
    }

    [Fact]
    public void Row_padding_is_removed_and_returned_pixels_are_owned()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8, 99, 99, 99, 99, 9, 10, 11, 12, 13, 14, 15, 16, 99, 99, 99, 99];
        var pinned = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var frame = WgcFrameLayout.CopyRows(pinned.AddrOfPinnedObject(), 12, 2, 2, 7);
            Array.Fill(source, (byte)0);

            Assert.Equal(Enumerable.Range(1, 16).Select(value => (byte)value), frame.Bgra32.ToArray());
            Assert.Equal(7, frame.CaptureGeneration);
        }
        finally { pinned.Free(); }
    }

    [Fact]
    public void Invalid_row_pitch_is_rejected_before_reading_native_memory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WgcFrameLayout.CopyRows((nint)1, 4, 2, 2, 1));
    }
}
