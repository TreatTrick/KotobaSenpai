using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class OverlayAnnotationTests
{
    [Theory]
    [InlineData(OverlayOutlineColorMode.Black, "#FF000000")]
    [InlineData(OverlayOutlineColorMode.White, "#FFFFFFFF")]
    public Task Manual_outline_color_and_width_change_the_visible_stroke(OverlayOutlineColorMode mode, string expected) => RunSta(() =>
    {
        var appearance = new OverlayAppearance(FuriganaColor: "#808080", OutlineColorMode: mode, OutlineThickness: 0.5);
        var shape = OverlayAnnotation.CreatePill(20, 4, OverlayColorRole.Furigana, new OverlayAnnotationStyle(appearance));
        var thin = Render(shape, 1);
        shape.ApplyStyle(new OverlayAnnotationStyle(appearance with { OutlineThickness = 3 }));
        var thick = Render(shape, 1);

        Assert.Contains(thick, color => color.ToString() == expected);
        Assert.True(thick.Count(color => color.A > 0) > thin.Count(color => color.A > 0));
        shape.ApplyStyle(new OverlayAnnotationStyle(appearance with { OutlineEnabled = false, OutlineThickness = 3 }));
        Assert.True(Render(shape, 1).Count(color => color.A > 0) < thick.Count(color => color.A > 0));
    });

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2d)]
    public Task Small_text_has_a_crisp_contrasting_outline_at_each_dpi(double scale) => RunSta(() =>
    {
        var style = new OverlayAnnotationStyle(new OverlayAppearance(FuriganaColor: "#000000"));
        var text = OverlayAnnotation.CreateText("語", 9, OverlayColorRole.Furigana, style, scale);
        var glyph = text.Glyph;
        var outlined = Render(text, scale);
        Assert.Contains(outlined, color => color.A > 200 && color.R > 200 && color.G > 200 && color.B > 200);
        text.ApplyStyle(new OverlayAnnotationStyle(style.Appearance with { OutlineEnabled = false }));
        var plain = Render(text, scale);
        Assert.DoesNotContain(plain, color => color.A > 200 && color.R > 200);
        text.ApplyStyle(new OverlayAnnotationStyle(style.Appearance with
            { OutlineColorMode = OverlayOutlineColorMode.White, OutlineThickness = 3 }));
        Assert.True(Render(text, scale).Count(color => color.A > 0) > outlined.Count(color => color.A > 0));
        Assert.Same(glyph, text.Glyph);
        Assert.False(text.IsHitTestVisible);
        Assert.Null(text.Effect);
    });

    [Fact]
    public Task Roles_have_independent_colors_and_hover_restores_the_saved_underline() => RunSta(() =>
    {
        var appearance = new OverlayAppearance("#000000", "#FFFFFF", "#123456", "#ABCDEF", false);
        var style = new OverlayAnnotationStyle(appearance);
        foreach (var (role, expected) in new[]
        {
            (OverlayColorRole.Furigana, "#FF000000"), (OverlayColorRole.High, "#FFFFFFFF"),
            (OverlayColorRole.Low, "#FF123456"), (OverlayColorRole.Underline, "#FFABCDEF"),
        })
        {
            var shape = OverlayAnnotation.CreatePill(20, 4, role, style);
            Assert.Contains(Render(shape, 1), color => color.ToString() == expected);
        }
        var underline = OverlayAnnotation.CreatePill(30, 2, OverlayColorRole.Underline, style);
        underline.Role = OverlayColorRole.Hover;
        Assert.Contains(Render(underline, 1), color => color.ToString() == "#FFFF4500");
        underline.Role = OverlayColorRole.Underline;
        Assert.Contains(Render(underline, 1), color => color.ToString() == "#FFABCDEF");
    });

    private static Color[] Render(FrameworkElement element, double scale)
    {
        if (element.Parent is Panel oldParent)
            oldParent.Children.Remove(element);
        var canvas = new Canvas { Width = 80, Height = 50 };
        canvas.Children.Add(element);
        Canvas.SetLeft(element, 5);
        Canvas.SetTop(element, 5);
        canvas.Measure(new Size(80, 50));
        canvas.Arrange(new Rect(0, 0, 80, 50));
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(80 * scale), (int)(50 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4)
            .Select(i => Color.FromArgb(pixels[i * 4 + 3], pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4])).ToArray();
    }

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
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
