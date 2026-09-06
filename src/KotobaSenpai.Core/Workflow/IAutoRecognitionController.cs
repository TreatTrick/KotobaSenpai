using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Core.Workflow;

/// <summary>Port for polling a target window and notifying the application when its recognition region changes.</summary>
public interface IAutoRecognitionController : IDisposable
{
    bool IsRunning { get; }

    void Start(WindowTarget target, Func<CancellationToken, Task>? onChange = null);

    void Stop();
}
