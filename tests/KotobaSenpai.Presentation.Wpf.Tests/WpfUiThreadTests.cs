using System.Windows.Threading;
using KotobaSenpai.Presentation.Wpf.Threading;

namespace KotobaSenpai.Presentation.Wpf.Tests;

public sealed class WpfUiThreadTests
{
    [Fact]
    public async Task Invoke_and_post_execute_on_the_owner_thread_and_propagate_callback_failures()
    {
        var ready = new TaskCompletionSource<(WpfUiThread Adapter, Dispatcher Dispatcher, int ThreadId)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            ready.SetResult((new WpfUiThread(dispatcher), dispatcher, Environment.CurrentManagedThreadId));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var owner = await ready.Task;
        try
        {
            Assert.False(owner.Adapter.CheckAccess());
            Assert.Equal(owner.ThreadId, owner.Adapter.Invoke(() => Environment.CurrentManagedThreadId));
            Assert.True(owner.Adapter.Invoke(owner.Adapter.CheckAccess));
            Assert.Throws<InvalidOperationException>(() => owner.Adapter.Invoke<int>(() => throw new InvalidOperationException("Callback failed.")));
            var posted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.Adapter.Post(() => posted.SetResult(Environment.CurrentManagedThreadId));
            Assert.Equal(owner.ThreadId, await posted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            owner.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The test dispatcher must finish shutdown.");
        }
    }
}
