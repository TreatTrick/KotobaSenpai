using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Capture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                await RunAsync(args.FirstOrDefault() ?? "Aokana EXTRA2");
                exitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally
            {
                foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
                app.Shutdown();
            }
        };
        app.Run();
        return exitCode;
    }

    private static async Task RunAsync(string gameTitle)
    {
        var label = new TextBlock { Foreground = Brushes.White, FontSize = 24, Margin = new Thickness(40) };
        var targetWindow = new Window
        {
            Title = "WGC changing target", Left = 40, Top = 40, Width = 360, Height = 260,
            Background = new SolidColorBrush(Color.FromRgb(192, 64, 32)), Content = label,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true
        };
        targetWindow.Show();
        var tick = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => label.Text = $"Frame {++tick:D4}";
        timer.Start();
        var target = ReadTarget(new WindowInteropHelper(targetWindow).Handle);
        using var capture = new WgcWindowFrameCapture();
        try
        {
            var cover = new Window
            {
                Title = "WGC test occluder", WindowStyle = WindowStyle.None,
                Left = target.Bounds.X - 12, Top = target.Bounds.Y - 12,
                Width = target.Bounds.Width + 24, Height = target.Bounds.Height + 24,
                Background = new SolidColorBrush(Color.FromRgb(17, 238, 51)), Topmost = true,
                ShowActivated = false, ShowInTaskbar = false
            };
            foreach (var style in new[] { "Framed", "Borderless", "Maximized", "Negative origin" })
            {
                cover.Hide();
                targetWindow.WindowState = WindowState.Normal;
                targetWindow.WindowStyle = style == "Borderless" ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                targetWindow.WindowState = style == "Maximized" ? WindowState.Maximized : WindowState.Normal;
                if (style == "Negative origin") SetWindowPos(target.Handle, 0, -40, 40, 450, 320, 0x0014);
                await Task.Delay(200);
                target = ReadTarget(target.Handle);
                var initial = await capture.CaptureAsync(target);
                var sampleX = Math.Max(4, -target.Bounds.X + 4);
                CheckPixel(initial, sampleX, 4, $"{style}: visible client pixel");
                cover.Show();
                SetWindowPos(new WindowInteropHelper(cover).Handle, new nint(-1), target.Bounds.X - 12, target.Bounds.Y - 12,
                    target.Bounds.Width + 24, target.Bounds.Height + 24, 0x0010);
                await Task.Delay(300);
                var covered = await capture.CaptureAsync(target);
                CheckPixel(covered, sampleX, 4, $"{style}: fully covered client pixel");
                var region = new ScreenRect(100, 100, 80, 40);
                var crop = await capture.CaptureAsync(target, region: region);
                if (crop.Width != 80 || crop.Height != 40) throw new InvalidOperationException("Incorrect crop dimensions.");
                CheckPixel(crop, 4, 4, $"{style}: covered crop");
                if (SHA256.HashData(initial.Bgra32.Span).SequenceEqual(SHA256.HashData(covered.Bgra32.Span)))
                    throw new InvalidOperationException("Changing target stopped updating while covered.");
            }
            cover.Hide();
            targetWindow.Hide();
            var game = new Win32WindowCatalog().ListVisibleWindows().FirstOrDefault(window => window.Title == gameTitle);
            if (game is not null)
            {
                var before = await capture.CaptureAsync(game);
                cover.Show();
                SetWindowPos(new WindowInteropHelper(cover).Handle, new nint(-1), game.Bounds.X - 12, game.Bounds.Y - 12,
                    game.Bounds.Width + 24, game.Bounds.Height + 24, 0x0010);
                await Task.Delay(300);
                var coveredGame = await capture.CaptureAsync(game);
                var greenPixels = 0;
                for (var i = 0; i < coveredGame.Bgra32.Length; i += 4)
                    if (coveredGame.Bgra32.Span[i] == 51 && coveredGame.Bgra32.Span[i + 1] == 238 && coveredGame.Bgra32.Span[i + 2] == 17) greenPixels++;
                if (greenPixels > coveredGame.Width * coveredGame.Height / 100)
                    throw new InvalidOperationException("Game capture contains the occluder.");
                Console.WriteLine($"PASS: {gameTitle} covered capture {coveredGame.Width}x{coveredGame.Height}; occluderPixels={greenPixels}; backgroundUpdated={!SHA256.HashData(before.Bgra32.Span).SequenceEqual(SHA256.HashData(coveredGame.Bgra32.Span))}");
            }
            else Console.WriteLine($"NOT RUN: {gameTitle} is not visible and restored.");
            Console.WriteLine($"PASS: native window geometry and occlusion; OS={Environment.OSVersion.Version}; DPI={GetDpiForWindow(target.Handle)}");
        }
        finally
        {
            timer.Stop();
        }
    }

    private static void CheckPixel(CapturedFrame frame, int x, int y, string stage)
    {
        var offset = (y * frame.Width + x) * 4;
        var p = frame.Bgra32.Span;
        if (Math.Abs(p[offset] - 32) > 3 || Math.Abs(p[offset + 1] - 64) > 3 || Math.Abs(p[offset + 2] - 192) > 3)
            throw new InvalidOperationException($"{stage}: expected target BGRA=(32,64,192), actual=({p[offset]},{p[offset + 1]},{p[offset + 2]}).");
        Console.WriteLine($"PASS: {stage}; {frame.Width}x{frame.Height}");
    }

    private static WindowTarget ReadTarget(nint hwnd)
    {
        GetClientRect(hwnd, out var rect);
        var origin = new Point();
        ClientToScreen(hwnd, ref origin);
        return new WindowTarget(hwnd, "Probe target", new ScreenRect(origin.X, origin.Y, rect.Right, rect.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
