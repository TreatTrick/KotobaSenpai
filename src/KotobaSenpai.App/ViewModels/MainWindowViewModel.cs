using CommunityToolkit.Mvvm.ComponentModel;
using KotobaSenpai.App.Japanese;

namespace KotobaSenpai.App.ViewModels;

/// <summary>Composes the main window's page state and coordinates its lifetime.</summary>
public sealed partial class MainWindowViewModel(SettingsPageViewModel settings, UniDicInstallController dictionary,
    OnboardingViewModel onboarding) : ObservableObject, IDisposable
{
    private bool _disposed;
    public SettingsPageViewModel Settings { get; } = settings;
    public UniDicInstallController Dictionary { get; } = dictionary;
    public OnboardingViewModel Onboarding { get; } = onboarding;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Selection))]
    private RecognitionPageViewModel? _recognition;
    public TargetSelectionViewModel? Selection => Recognition?.Selection;

    public async Task StopAsync()
    {
        var operations = new[]
        {
            Onboarding.InitializeCommand.ExecutionTask, Onboarding.NextCommand.ExecutionTask,
            Selection?.SelectWindowCommand.ExecutionTask, Selection?.RetryPreviewCommand.ExecutionTask,
            Recognition?.RecognizeCommand.ExecutionTask, Settings.Llm.CheckConfigurationCommand.ExecutionTask,
            Settings.Llm.FetchModelsCommand.ExecutionTask,
        }.OfType<Task>().Where(task => !task.IsCompleted).ToArray();
        Dispose();
        await Task.WhenAll(operations);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Onboarding.Dispose();
        Recognition?.Dispose();
        Settings.Dispose();
    }
}
