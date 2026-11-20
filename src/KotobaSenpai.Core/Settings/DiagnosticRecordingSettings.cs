namespace KotobaSenpai.Core.Settings;

/// <summary>Shared storage contract for the opt-in diagnostic files.</summary>
public static class DiagnosticRecordingSettings
{
    public const string EnabledKey = "DiagEnabled";

    public static bool IsEnabled(ISettingsService settings)
        => string.Equals(settings.GetValue(EnabledKey), "true", StringComparison.OrdinalIgnoreCase);
}
