using System.Windows.Controls;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Tests;

public sealed class OverlayAnnotationLayerTests
{
    [Fact]
    public async Task Appending_a_sentence_retains_existing_furigana_and_underlines_without_a_native_window()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var target = new WindowTarget((nint)42, "Target", new ScreenRect(100, 100, 700, 400));
                var layer = new OverlayAnnotationLayer();
                GroupedWord Word(string surface, string reading, int row) => new GroupedWord(new Token(
                    surface, surface, surface, reading, reading, reading, new("noun", "", "", ""), "", "", "1", 0),
                    new ScreenRect(180, 160 + row * 90, 90, 40)).WithSegmentId($"s{row}-{row}");
                layer.Render(WordOverlaySession.Start(target, [Word("僕", "ボク", 0)], underlineSegmentIds: ["s0-0"]), 1, 1d / 3);
                var canvas = layer.Canvas;
                var original = canvas.Children.OfType<OverlayAnnotation>().ToArray();
                Assert.Contains(original, annotation => annotation.Glyph is not null);
                Assert.Contains(original, annotation => annotation.Role == OverlayColorRole.Underline);

                layer.Render(WordOverlaySession.Start(target,
                    [Word("僕", "ボク", 0), Word("職員", "ショクイン", 1)], underlineSegmentIds: ["s0-0"]), 1, 1d / 3);

                Assert.All(original, annotation => Assert.Contains(annotation, canvas.Children.OfType<OverlayAnnotation>()));
                Assert.True(canvas.Children.Count > original.Length);
                var secondLine = canvas.Children.OfType<OverlayAnnotation>().Except(original).ToArray();
                layer.Render(WordOverlaySession.Start(target,
                    [Word("僕", "ボク", 0), Word("職員", "ショクイン", 1)], underlineSegmentIds: ["s0-0", "s1-1"]), 1, 1d / 3);
                Assert.All(original, annotation => Assert.Contains(annotation, canvas.Children.OfType<OverlayAnnotation>()));
                Assert.DoesNotContain(secondLine, annotation => canvas.Children.Contains(annotation));
                Assert.Equal([80d, 80d], canvas.Children.OfType<OverlayAnnotation>()
                    .Where(annotation => annotation.Role == OverlayColorRole.Underline).Select(Canvas.GetLeft));

                layer.Render(WordOverlaySession.Start(target, []), 1, 1d / 3);
                Assert.Empty(canvas.Children);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }
}
