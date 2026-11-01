namespace KotobaSenpai.Core.Settings;

/// <summary>Persists the capture border preference and publishes committed changes.</summary>
public sealed class CaptureBorderPreferences(ISettingsService settings)
{
    public const string ShowBorderKey = "ShowCaptureBorder";
    private bool _showBorder = bool.TryParse(settings.GetValue(ShowBorderKey), out var enabled) && enabled;

    public event EventHandler? Changed;

    public bool ShowBorder
    {
        get => Volatile.Read(ref _showBorder);
        set
        {
            if (value == ShowBorder) return;
            settings.SetValue(ShowBorderKey, value ? "true" : "false");
            Volatile.Write(ref _showBorder, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
