using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Presentation.Wpf.Guidance;

namespace KotobaSenpai.App.Guidance.Presentation;

/// <summary>Maps application flow state to the localized Windows presentation contract.</summary>
public sealed class TourPresentation : ITourPresentation
{
    private readonly TourSession _session;
    private readonly IStringLocalizer _localizer;
    private bool _disposed;

    public TourPresentation(TourSession session, IStringLocalizer localizer)
    {
        _session = session;
        _localizer = localizer;
        session.Changed += OnChanged;
        localizer.CultureChanged += OnChanged;
    }

    public GuidanceTarget? Target => _session.Current?.Target;
    public string Text => _session.Current is { } step ? _localizer.Get(step.TextKey) : string.Empty;
    public string AcknowledgeText => _localizer.Get(ResourceKeys.Guide_Understood);
    public event EventHandler? Changed;
    public void Start() { if (!_disposed) _session.Start(); }
    public void Advance() { if (!_disposed) _session.Advance(); }
    public void Stop() => _session.Stop();
    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Changed -= OnChanged;
        _localizer.CultureChanged -= OnChanged;
        _session.Stop();
    }
}
