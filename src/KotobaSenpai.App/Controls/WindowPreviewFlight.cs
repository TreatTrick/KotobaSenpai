using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.App.Controls;

internal static class WindowPreviewFlight
{
    internal static Rect Fit(Rect container, double aspectRatio)
    {
        var width = Math.Min(container.Width, container.Height * aspectRatio);
        var height = width / aspectRatio;
        return new Rect(container.X + (container.Width - width) / 2, container.Y + (container.Height - height) / 2, width, height);
    }

    internal static BitmapSource CreateThumbnail(BitmapSource source, int maxPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixels);
        var scale = Math.Min(1, (double)maxPixels / Math.Max(source.PixelWidth, source.PixelHeight));
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var stride = checked(scaled.PixelWidth * 4);
        var pixels = new byte[checked(stride * scaled.PixelHeight)];
        scaled.CopyPixels(pixels, stride, 0);
        // Copy the reduced pixels so the thumbnail cannot retain the full-size TransformedBitmap source.
        var thumbnail = BitmapSource.Create(scaled.PixelWidth, scaled.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        thumbnail.Freeze();
        return thumbnail;
    }

    internal static async Task PlayAsync(BitmapSource source, ScreenRect start, FrameworkElement destination, CancellationToken cancellationToken)
    {
        if (!SystemParameters.ClientAreaAnimation || !destination.IsVisible || cancellationToken.IsCancellationRequested) return;
        var target = GetDestination(destination, source);
        if (target.IsEmpty || target.Width <= 0 || target.Height <= 0) return;
        var desktop = DesktopOverlayWindow.DesktopBounds;
        var surface = new DesktopOverlayWindow();
        var canvas = new Canvas();
        var image = new Image { Source = source, Stretch = Stretch.Uniform };
        canvas.Children.Add(image);
        surface.Content = canvas;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = Stopwatch.StartNew();
        var origin = new Rect(start.X, start.Y, start.Width, start.Height);
        var lift = Math.Min(100, (target.TopLeft - origin.TopLeft).Length * 0.12);

        void Render(object? sender, EventArgs e)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested || !destination.IsVisible
                    || DesktopOverlayWindow.DesktopBounds != desktop || GetDestination(destination, source) != target)
                {
                    completed.TrySetResult();
                    return;
                }
                var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / 400, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                var pixels = new Rect(
                    origin.X + (target.X - origin.X) * eased,
                    origin.Y + (target.Y - origin.Y) * eased - 4 * eased * (1 - eased) * lift,
                    origin.Width + (target.Width - origin.Width) * eased,
                    origin.Height + (target.Height - origin.Height) * eased);
                var local = surface.FromScreenPixels(pixels);
                Canvas.SetLeft(image, local.X);
                Canvas.SetTop(image, local.Y);
                image.Width = local.Width;
                image.Height = local.Height;
                if (progress >= 1) completed.TrySetResult();
            }
            catch (Exception exception) { completed.TrySetException(exception); }
        }

        try
        {
            surface.ShowOverDesktop();
            Render(null, EventArgs.Empty);
            CompositionTarget.Rendering += Render;
            using var registration = cancellationToken.Register(() => completed.TrySetResult());
            await completed.Task;
        }
        finally
        {
            CompositionTarget.Rendering -= Render;
            image.Source = null;
            surface.Close();
        }
    }

    private static Rect GetDestination(FrameworkElement destination, BitmapSource source)
    {
        if (!destination.IsVisible || destination.ActualWidth <= 0 || destination.ActualHeight <= 0) return Rect.Empty;
        var topLeft = destination.PointToScreen(new Point());
        var bottomRight = destination.PointToScreen(new Point(destination.ActualWidth, destination.ActualHeight));
        return Fit(new Rect(topLeft, bottomRight), (double)source.PixelWidth / source.PixelHeight);
    }
}
