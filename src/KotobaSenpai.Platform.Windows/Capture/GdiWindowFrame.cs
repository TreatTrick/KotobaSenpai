using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>
/// Phase-one capture adapter: uses GDI to copy the target window's current frame from the screen. Serves as a compatible
/// fallback for Windows.Graphics.Capture and can be swapped under the same port without affecting the domain or UI
/// contracts.
/// </summary>
internal sealed class GdiWindowFrame : IDisposable
{
    private readonly Bitmap _bitmap;

    private GdiWindowFrame(Bitmap bitmap) => _bitmap = bitmap;

    public int Width => _bitmap.Width;

    public int Height => _bitmap.Height;

    public static GdiWindowFrame Capture(WindowTarget target, ScreenRect? region = null)
    {
        int srcX = target.Bounds.X, srcY = target.Bounds.Y, w = target.Bounds.Width, h = target.Bounds.Height;
        if (region is { } r)
        {
            // Capture only the region's screen rectangle directly, avoiding copying the whole window then cropping.
            srcX = target.Bounds.X + r.X;
            srcY = target.Bounds.Y + r.Y;
            w = r.Width;
            h = r.Height;
        }
        var bitmap = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var destination = Graphics.FromImage(bitmap);
        destination.CopyFromScreen(srcX, srcY, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        return new GdiWindowFrame(bitmap);
    }

    public byte[] ToBgraBytes()
    {
        var data = _bitmap.LockBits(
            new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * _bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }
    }

    public void Dispose() => _bitmap.Dispose();
}

/// <summary>GDI implementation of the <see cref="IWindowFrameCapture"/> port.</summary>
public sealed class GdiWindowFrameCapture : IWindowFrameCapture
{
    public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var frame = GdiWindowFrame.Capture(target, region);
        return Task.FromResult(new CapturedFrame(frame.Width, frame.Height, frame.ToBgraBytes()));
    }
}
