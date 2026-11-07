using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Presentation.Wpf.Capture;

/// <summary>Picks a top-level window without sending the selection gesture to the source application.</summary>
public sealed class Win32WindowPicker(IStringLocalizer localizer, ILogger logger) : IWindowPicker, IDisposable
{
    private int _active;
    private RainbowWindow? _highlight;
    private nint _highlightTarget;

    public void Highlight(WindowTarget? target) => Highlight(target, Environment.ProcessId);

    internal void Highlight(WindowTarget? target, int excludedProcessId)
    {
        // Keep a retired binding pinned after identity loss until the selection is explicitly reset.
        if (_highlightTarget == (target?.Handle ?? 0)) return;
        _highlight?.Close();
        _highlight = null;
        _highlightTarget = 0;
        if (target is null || WindowPickerCandidates.ReadCandidate(target.Handle, excludedProcessId) is not { } candidate) return;
        _highlightTarget = target.Handle;
        var outline = new RainbowWindow();
        _highlight = outline;
        outline.Closed += (_, _) => { if (ReferenceEquals(_highlight, outline)) _highlight = null; };
        outline.Follow(candidate, logger);
    }

    public void Dispose() => Highlight(null);

    public async Task<WindowTarget?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("A window picking session is already active.");
        RainbowWindow? outline = null;
        try
        {
            Highlight(null);
            outline = new RainbowWindow(localizer.Get("WindowPicker_Hint"));
            outline.ShowOverDesktop();
            var session = new WindowPickingSession(logger, cancellationToken);
            WindowPickHover? latest = null;
            var updateQueued = 0;
            var finished = 0;
            var surface = outline;
            void UpdateHover(object? sender, WindowPickHover hover)
            {
                Volatile.Write(ref latest, hover);
                if (Interlocked.Exchange(ref updateQueued, 1) != 0) return;
                surface.Dispatcher.BeginInvoke(() =>
                {
                    Interlocked.Exchange(ref updateQueued, 0);
                    if (!session.IsActive || Volatile.Read(ref finished) != 0 || Volatile.Read(ref latest) is not { } current) return;
                    try { surface.Update(current.Candidate, current.X, current.Y); }
                    catch (Exception exception) { session.Fail(exception); }
                });
            }
            session.HoverChanged += UpdateHover;
            try { return await session.RunAsync(); }
            finally
            {
                Volatile.Write(ref finished, 1);
                session.HoverChanged -= UpdateHover;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new WindowsPlatformException(ErrorCodes.WindowSelectionFailed, "Desktop window picking failed.", exception);
        }
        finally
        {
            outline?.Close();
            Volatile.Write(ref _active, 0);
        }
    }
}
