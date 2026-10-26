using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.TestSupport;

/// <summary>Convenience batch operations for isolated settings test doubles.</summary>
internal abstract class SettingsStub : ISettingsService
{
    public abstract string? GetValue(string key);
    public abstract void SetValue(string key, string? value);

    // ponytail: these stubs are single-threaded; concurrency and persistence tests use the production SettingsService.
    public IReadOnlyDictionary<string, string?> GetSnapshot(IEnumerable<string> keys)
        => keys.ToDictionary(key => key, GetValue, StringComparer.Ordinal);

    public virtual bool TryUpdate(IReadOnlyDictionary<string, string?> expected, IReadOnlyDictionary<string, string?> changes)
    {
        if (expected.Any(entry => GetValue(entry.Key) != entry.Value))
            return false;
        foreach (var entry in changes)
            SetValue(entry.Key, entry.Value);
        return true;
    }
}
