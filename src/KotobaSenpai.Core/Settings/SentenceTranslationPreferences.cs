namespace KotobaSenpai.Core.Settings;

/// <summary>Persists the sentence translation preference and publishes committed changes.</summary>
public sealed class SentenceTranslationPreferences(ISettingsService settings)
{
    public const string EnabledKey = "SentenceTranslationEnabled";
    private bool _enabled = !bool.TryParse(settings.GetValue(EnabledKey), out var enabled) || enabled;

    public event EventHandler? Changed;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set
        {
            if (value == Enabled) return;
            settings.SetValue(EnabledKey, value ? "true" : "false");
            Volatile.Write(ref _enabled, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
