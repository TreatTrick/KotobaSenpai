using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>Owned BGRA bitmap operations for Windows-only OCR preprocessing.</summary>
internal static class OcrBitmap
{
    internal static Bitmap FromBgra(ReadOnlyMemory<byte> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = checked(width * 4);
        if (pixels.Length != checked(rowBytes * height))
            throw new ArgumentException("The BGRA buffer must match the image dimensions.", nameof(pixels));

        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var source = pixels.ToArray();
                for (var y = 0; y < height; y++)
                    Marshal.Copy(source, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
            finally { image.UnlockBits(data); }
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    internal static Bitmap Resize(Bitmap image, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(resized);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using var attributes = new ImageAttributes();
            // Mirror boundary pixels so the resize filter does not introduce transparent edges.
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(image, new Rectangle(0, 0, width, height),
                0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            return resized;
        }
        catch
        {
            resized.Dispose();
            throw;
        }
    }

    internal static byte[] CopyPixels(Bitmap image)
    {
        var rowBytes = checked(image.Width * 4);
        var pixels = new byte[checked(rowBytes * image.Height)];
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
        }
        finally { image.UnlockBits(data); }
        return pixels;
    }
}
