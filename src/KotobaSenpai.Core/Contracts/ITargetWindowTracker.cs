using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Publishes event-driven state changes for the currently tracked target window.</summary>
public interface ITargetWindowTracker : IDisposable
{
    event EventHandler<TargetWindowSnapshot>? Changed;

    TargetWindowSnapshot? Current { get; }

    /// <summary>Starts tracking the selected target, or refreshes it without rebuilding hooks when already attached.</summary>
    TargetWindowSnapshot Attach(WindowTarget target);

    /// <summary>Queries and publishes the latest state for the currently selected target.</summary>
    TargetWindowSnapshot Refresh();

    /// <summary>Stops tracking the selected target and clears its snapshot.</summary>
    void Detach();
}
