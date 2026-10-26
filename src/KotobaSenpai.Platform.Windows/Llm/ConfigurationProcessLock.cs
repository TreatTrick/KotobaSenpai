using System.Security.Principal;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>One writable settings owner per Windows user, across interactive sessions and installations.</summary>
public sealed class ConfigurationProcessLock : IDisposable
{
    private readonly Mutex _mutex;
    private ConfigurationProcessLock(Mutex mutex) => _mutex = mutex;

    public static ConfigurationProcessLock? TryAcquire(string? name = null)
    {
        var mutex = new Mutex(false, name ?? @"Global\KotobaSenpai.Settings." + WindowsIdentity.GetCurrent().User!.Value);
        try
        {
            try
            {
                if (mutex.WaitOne(0))
                    return new(mutex);
            }
            catch (AbandonedMutexException) { return new(mutex); }
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
