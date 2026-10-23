namespace KotobaSenpai.App.ViewModels;

/// <summary>Composes the main window's page state and coordinates its lifetime.</summary>
public sealed class MainWindowViewModel(RecognitionPageViewModel recognition, SettingsPageViewModel settings) : IDisposable
{
    public RecognitionPageViewModel Recognition { get; } = recognition;
    public SettingsPageViewModel Settings { get; } = settings;

    public void Dispose()
    {
        Recognition.Dispose();
        Settings.Dispose();
    }
}
