using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KotobaSenpai.App.Controls;

namespace KotobaSenpai.App.Tests;

public sealed class ColorSpectrumTests
{
    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(255, 255, 0, 60)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 255, 255, 180)]
    [InlineData(0, 0, 255, 240)]
    [InlineData(255, 0, 255, 300)]
    [InlineData(128, 128, 128, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 0)]
    public void Hsv_round_trip_preserves_rgb_values(byte red, byte green, byte blue, double expectedHue)
    {
        var color = Color.FromRgb(red, green, blue);
        var hsv = ColorSpectrum.ToHsv(color);
        Assert.Equal(expectedHue, hsv.Hue, 5);
        Assert.Equal(color, ColorSpectrum.FromHsv(hsv.Hue, hsv.Saturation, hsv.Brightness));
    }

    [Fact]
    public Task Two_dimensional_selection_clamps_and_preserves_hue_through_black_and_gray() => RunSta(() =>
    {
        var spectrum = new ColorSpectrum();
        spectrum.Measure(new Size(200, 200));
        spectrum.Arrange(new Rect(0, 0, 200, 200));
        spectrum.Hue = 120;
        spectrum.SelectAt(new Point(500, -10));
        Assert.Equal(Colors.Lime, spectrum.Color);
        spectrum.SelectAt(new Point(-10, -10));
        Assert.Equal(Colors.White, spectrum.Color);
        Assert.Equal(120, spectrum.Hue);
        spectrum.SelectAt(new Point(500, 500));
        Assert.Equal(Colors.Black, spectrum.Color);
        var updates = 0;
        spectrum.ColorChanged += (_, _) => updates++;
        spectrum.SelectAt(new Point(500, 500));
        Assert.Equal(1, updates);
        spectrum.Hue = 240;
        Assert.Equal(Colors.Black, spectrum.Color);
        spectrum.SelectAt(new Point(500, -10));
        Assert.Equal(Colors.Blue, spectrum.Color);
        spectrum.Color = Colors.Gray;
        Assert.Equal(240, spectrum.Hue);
        spectrum.SelectAt(new Point(500, -10));
        Assert.Equal(Colors.Blue, spectrum.Color);
    });

    [Fact]
    public Task Rendered_field_matches_the_selected_saturation_and_brightness() => RunSta(() =>
    {
        var spectrum = new ColorSpectrum { Hue = 0 };
        spectrum.Measure(new Size(200, 200));
        spectrum.Arrange(new Rect(0, 0, 200, 200));
        var bitmap = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(spectrum);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(100, 100, 1, 1), pixel, 4, 0);
        spectrum.SelectAt(new Point(100, 100));

        Assert.Equal(255, pixel[3]);
        Assert.InRange(Math.Abs(pixel[2] - spectrum.Color.R), 0, 3);
        Assert.InRange(Math.Abs(pixel[1] - spectrum.Color.G), 0, 3);
        Assert.InRange(Math.Abs(pixel[0] - spectrum.Color.B), 0, 3);
    });

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
