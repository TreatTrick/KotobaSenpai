using KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;
using Microsoft.ML.OnnxRuntime;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class OnnxRunCancellationTests
{
    [Fact]
    public void Pre_cancelled_inference_never_enters_native_work()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var entered = false;

        Assert.Throws<OperationCanceledException>(() => OnnxRunCancellation.Execute(cancellation.Token, _ => { entered = true; return 1; }));

        Assert.False(entered);
    }

    [Fact]
    public void Cancellation_terminates_native_run_and_surfaces_managed_cancellation()
    {
        using var cancellation = new CancellationTokenSource();

        var nativeFailure = NativeFailure();
        var exception = Assert.Throws<OperationCanceledException>(() => OnnxRunCancellation.Execute<int>(cancellation.Token, options =>
        {
            cancellation.Cancel();
            Assert.True(options.Terminate);
            throw nativeFailure;
        }));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void Native_failures_remain_failures_when_cancellation_was_not_requested()
    {
        var expected = NativeFailure();

        var actual = Assert.Throws<OnnxRuntimeException>(() => OnnxRunCancellation.Execute<int>(CancellationToken.None, _ => throw expected));

        Assert.Same(expected, actual);
    }
    private static OnnxRuntimeException NativeFailure()
        => Assert.Throws<OnnxRuntimeException>(() => new InferenceSession(Array.Empty<byte>()));
}
