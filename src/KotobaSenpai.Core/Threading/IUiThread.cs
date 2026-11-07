namespace KotobaSenpai.Core.Threading;

/// <summary>Dispatches callbacks to the owning UI thread without exposing a presentation framework.</summary>
public interface IUiThread
{
    bool CheckAccess();
    void Post(Action action);

    /// <summary>Runs synchronously on the owner thread and propagates callback or dispatcher shutdown failures.</summary>
    T Invoke<T>(Func<T> action);
}

public static class UiThreadExtensions
{
    public static void Invoke(this IUiThread thread, Action action)
        => thread.Invoke(() => { action(); return 0; });
}
