using KotobaSenpai.Core.Threading;
using System.Collections.Concurrent;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>Runs synchronous fixture calls directly and keeps timer posts under explicit test control.</summary>
internal sealed class InlineUiThread : IUiThread
{
    private readonly ConcurrentQueue<Action> _posted = new();
    public bool CheckAccess() => true;
    public int PendingCount => _posted.Count;
    public void Post(Action action) => _posted.Enqueue(action);
    public T Invoke<T>(Func<T> action) => action();
    public void RunPosted()
    {
        while (_posted.TryDequeue(out var action)) action();
    }
}
