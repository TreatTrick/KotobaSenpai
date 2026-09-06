using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.AutoRecognition;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Platform.Windows.Tests.Capture;

public sealed class FrameDifferenceTests
{
    [Fact]
    public void Reports_no_change_when_the_frame_is_identical()
    {
        var frame = new CapturedFrame(2, 1, new byte[8]);

        Assert.False(FrameDifference.Exceeds(frame, frame, 0.01));
    }

    [Fact]
    public void Reports_change_when_the_changed_pixel_ratio_reaches_the_threshold()
    {
        var previous = new CapturedFrame(2, 1, new byte[8]);
        var currentBytes = new byte[8];
        currentBytes[0] = 255;
        var current = new CapturedFrame(2, 1, currentBytes);

        Assert.True(FrameDifference.Exceeds(previous, current, 0.5));
    }

    [Fact]
    public void Reports_change_when_frame_dimensions_differ()
    {
        var previous = new CapturedFrame(1, 1, new byte[4]);
        var current = new CapturedFrame(2, 1, new byte[8]);

        Assert.True(FrameDifference.Exceeds(previous, current, 1));
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData("500", 500)]
    [InlineData("50", 100)]
    public void Normalizes_the_polling_interval(string? raw, int expected)
    {
        Assert.Equal(expected, AutoRecognitionController.NormalizeIntervalMilliseconds(raw));
    }
}
