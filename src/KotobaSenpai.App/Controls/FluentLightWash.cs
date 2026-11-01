using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KotobaSenpai.App.Controls;

/// <summary>Cached, subtly dithered light textures avoid visible low-opacity gradient bands.</summary>
internal static class FluentLightWash
{
    internal const double Radius = 220;
    private const int TextureSize = 512;
    private const double PeakOpacity = 0.065;
    private static readonly BitmapSource Dark = Create(Color.FromRgb(179, 200, 255));
    private static readonly BitmapSource Light = Create(Color.FromRgb(76, 112, 235));

    internal static BitmapSource ForTheme(bool dark) => dark ? Dark : Light;

    private static BitmapSource Create(Color color)
    {
        var pixels = new byte[TextureSize * TextureSize * 4];
        for (var y = 0; y < TextureSize; y++)
        for (var x = 0; x < TextureSize; x++)
        {
            var dx = (x + 0.5) * 2 / TextureSize - 1;
            var dy = (y + 0.5) * 2 / TextureSize - 1;
            var remaining = Math.Max(0, 1 - dx * dx - dy * dy);
            // Value and slope reach zero at the perimeter instead of meeting a linear cutoff.
            var alpha = PeakOpacity * 255 * remaining * remaining * remaining;
            // Stable stochastic rounding breaks circular quantization bands without animated noise.
            var hash = unchecked((uint)x * 374761393u + (uint)y * 668265263u);
            hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
            var noise = ((hash ^ (hash >> 16)) & 0xffff) / 65536d;
            var index = (y * TextureSize + x) * 4;
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = (byte)Math.Floor(alpha + noise);
        }
        var texture = BitmapSource.Create(TextureSize, TextureSize, 96, 96, PixelFormats.Bgra32, null, pixels, TextureSize * 4);
        texture.Freeze();
        return texture;
    }
}
