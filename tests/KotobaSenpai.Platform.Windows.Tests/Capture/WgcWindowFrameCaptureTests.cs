using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Platform.Windows.Tests.Capture;

public sealed class WgcWindowFrameCaptureTests
{
    private static WindowTarget MissingTarget() => new((nint)int.MaxValue, "Missing", new ScreenRect(0, 0, 100, 100));

    [Fact]
    public async Task Precancelled_requests_do_not_initialize_native_capture()
    {
        using var capture = new WgcWindowFrameCapture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => capture.CaptureAsync(MissingTarget(), cancellation.Token));
    }

    [Fact]
    public async Task Invalid_targets_report_a_stable_error_and_can_be_retried_after_stop()
    {
        using var capture = new WgcWindowFrameCapture();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = await Assert.ThrowsAsync<WindowsPlatformException>(() => capture.CaptureAsync(MissingTarget()));
            Assert.Equal(ErrorCodes.TargetWindowUnavailable, error.ErrorCode);
            capture.Stop();
            capture.Stop();
        }
    }

    [Fact]
    public async Task Disposed_adapters_reject_new_requests_and_dispose_is_idempotent()
    {
        var capture = new WgcWindowFrameCapture();
        capture.Stop();
        capture.Dispose();
        capture.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => capture.CaptureAsync(MissingTarget()));
    }
}
