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

    public static GdiWindowFrame Capture(WindowTarget target)
    {
        var bitmap = new Bitmap(target.Bounds.Width, target.Bounds.Height, PixelFormat.Format32bppPArgb);
        using var destination = Graphics.FromImage(bitmap);
        destination.CopyFromScreen(target.Bounds.X, target.Bounds.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
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
    public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var frame = GdiWindowFrame.Capture(target);
        return Task.FromResult(new CapturedFrame(frame.Width, frame.Height, frame.ToBgraBytes()));
    }
}
