namespace KotobaSenpai.Core.Shortcuts;

public interface IGlobalShortcutRegistrar : IDisposable
{
    event EventHandler<int>? Triggered;
    bool TryRegister(int registrationId, ShortcutGesture gesture, out int error);
    void Unregister(int registrationId);
}
