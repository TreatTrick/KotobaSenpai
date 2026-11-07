using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Onboarding;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.App.Startup;

internal sealed record RecognitionRuntime(RecognitionPageViewModel Page, WpfOverlayRenderer Renderer);

/// <summary>Prepares recognition lazily and publishes it without depending on the window shell or a container.</summary>
internal sealed class RecognitionStartup(UniDicInstallController dictionary, LlmSettingsViewModel llm,
    LlmConfigurationService configuration, Func<CancellationToken, Task<RecognitionRuntime>> createRuntime,
    ILogger logger) : IDisposable
{
    private bool _disposed;
    public RecognitionRuntime? Current { get; private set; }
    public event EventHandler<RecognitionRuntime>? Prepared;

    public OnboardingReadiness ReadReadiness()
    {
        var target = Current?.Page.Selection;
        return new(dictionary.CheckInstalled(), llm.IsConfigurationUsable && configuration.GetStatus().Verified
            && !string.IsNullOrWhiteSpace(llm.Endpoint) && !string.IsNullOrWhiteSpace(llm.ApiKey)
            && !string.IsNullOrWhiteSpace(llm.Model),
            target?.SelectedWindow is not null, target?.IsTargetReady == true,
            target?.IsSelectingWindow == true, llm.IsCheckingConfiguration || llm.IsFetchingModels);
    }

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            await dictionary.InstallAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!dictionary.IsInstalled)
                throw new InvalidOperationException("Dictionary preparation failed.", dictionary.Failure);
            if (Current is not null) return;

            var runtime = await createRuntime(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            Current = runtime;
            Prepared?.Invoke(this, runtime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to prepare recognition services.");
            throw;
        }
    }

    public Task StartRecognitionAsync()
        => (Current ?? throw new InvalidOperationException("Recognition is not prepared.")).Page.StartRecognitionAsync();

    public void RefreshTarget() => Current?.Page.Selection.RefreshTarget();
    public void RefreshAppearance() => Current?.Renderer.RefreshAppearance();
    public void Dispose() => _disposed = true;
}
