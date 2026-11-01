using KotobaSenpai.Core.Overlay;

namespace KotobaSenpai.Platform.Windows.Guidance;

/// <summary>Supplies localized steps to a Windows host without exposing application flow types.</summary>
public interface ITourPresentation : IDisposable
{
    GuidanceTarget? Target { get; }
    string Text { get; }
    string AcknowledgeText { get; }
    event EventHandler? Changed;
    void Start();
    void Advance();
    void Stop();
}
