using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.TargetWindow;
using System.Windows;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>
/// WPF implementation of the <see cref="IOverlayRenderer"/> port: a transparent, target-relative, non-activating, click-through
/// tool window that draws one underline for each local merged word. Hovering a local word recolors its underline and shows
/// its LLM meaning popup; hovering a group recolors the underlines of its member words and shows the group detail popup.
/// Annotation clicks pass through; the detail popup receives mouse input without activating.
/// </summary>
public sealed class WpfOverlayRenderer : IOverlayRenderer, IDisposable
{
    private readonly IStringLocalizer? _localizer;
    private readonly ISettingsService? _settings;
    private readonly ITargetWindowTracker? _tracker;
    private readonly ILogger? _logger;
    private readonly SentenceTranslationPreferences? _sentenceTranslation;
    private OverlayWindow? _window;
    private bool _disposed;

    public WpfOverlayRenderer(
        IStringLocalizer? localizer = null,
        ISettingsService? settings = null,
        ITargetWindowTracker? tracker = null,
        ILogger? logger = null,
        SentenceTranslationPreferences? sentenceTranslation = null)
    {
        _localizer = localizer;
        _settings = settings;
        _tracker = tracker;
        _logger = logger;
        _sentenceTranslation = sentenceTranslation ?? (settings is null ? null : new SentenceTranslationPreferences(settings));
        if (_sentenceTranslation is not null)
            _sentenceTranslation.Changed += OnSentenceTranslationChanged;
    }

    private void OnSentenceTranslationChanged(object? sender, EventArgs e)
        => _window?.Dispatcher.Invoke(() => _window?.SetSentenceTranslationEnabled(_sentenceTranslation!.Enabled));

    public void Show(WordOverlaySession session, Func<bool>? canRender = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (canRender?.Invoke() == false)
            {
                _logger?.Log(LogLevel.Debug, null, "[OVERLAY] Publication rejected because recognition was cancelled or superseded: sessionId={0}, targetHandle={1}.",
                    session.Id, session.Target.Handle);
                return;
            }
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[OVERLAY] Show requested: sessionId={0}, targetHandle={1}, words={2}, underlines={3}, phraseGroups={4}, trackerAttached={5}, sourceTargetBounds={6}.",
                session.Id,
                session.Target.Handle,
                session.Words.Count,
                session.Lines.Count,
                session.PhraseGroups.Count,
                _tracker is not null,
                session.Target.Bounds);
            _window ??= new OverlayWindow(_localizer, _tracker, _logger);
            _window.SetSentenceTranslationEnabled(_sentenceTranslation?.Enabled ?? true);
            _window.ApplyAppearance(ResolveAppearance());
            _window.Render(session, ResolveFontScale());
        });
    }

    /// <summary>Reads the configurable furigana font scale (proportion of OCR text height); missing/invalid falls back to the default 1/3.</summary>
    private double ResolveFontScale()
        => FuriganaFontSettings.ResolveFontScale(_settings?.GetValue(FuriganaFontSettings.FontScaleKey));

    private OverlayAppearance ResolveAppearance()
        => _settings is null ? OverlayAppearanceSettings.Default : OverlayAppearanceSettings.Load(_settings);

    public void RefreshAppearance()
        => Application.Current.Dispatcher.Invoke(() => _window?.ApplyAppearance(ResolveAppearance()));

    public void Hide()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logger?.LogInformation("[OVERLAY] Explicit Hide requested.");
            _window?.ClearSession();
            if (_window?.IsVisible == true) _window.Hide();
        });
    }

    public void Suspend()
        => Application.Current.Dispatcher.Invoke(() => _window?.SuspendPresentation());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sentenceTranslation is not null) _sentenceTranslation.Changed -= OnSentenceTranslationChanged;
        if (_window is not null)
        {
            _window.Dispatcher.Invoke(() => _window.Close());
            _window = null;
        }
    }
}
