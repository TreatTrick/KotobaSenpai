using KotobaSenpai.Core.Logging;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class WindowPickingSessionTests
{
    [Fact]
    public async Task Failure_before_the_worker_queue_exists_does_not_lose_its_exit_signal()
    {
        var expected = new InvalidOperationException("The picking surface failed before startup.");
        var session = new WindowPickingSession(new SilentLogger(), CancellationToken.None);
        session.Fail(expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(expected, actual);
        Assert.False(session.IsActive);
    }

    [Fact]
    public async Task Pre_cancelled_sessions_finish_without_installing_input_hooks()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = new WindowPickingSession(new SilentLogger(), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(session.IsActive);
    }

    private sealed class SilentLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    }
}
