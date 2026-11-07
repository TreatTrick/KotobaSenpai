using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Capture;

/// <summary>Presents a retained window-selection outline and hint without owning native input hooks.</summary>
internal sealed class RainbowWindow : DesktopOverlayWindow
{
    private readonly Canvas _canvas = new();
    private readonly RainbowOutline _outline = new();
    private readonly Border _hint;

    public RainbowWindow(string? hint = null)
    {
        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 25, 28, 36)), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new TextBlock { Text = hint ?? string.Empty, Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap },
            Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible,
        };
        _canvas.Children.Add(_outline);
        _canvas.Children.Add(_hint);
        Content = _canvas;
    }

    internal void Follow(WindowPickerCandidate selected, ILogger logger)
    {
        var handle = selected.Target.Handle;
        var desktop = DesktopBounds;
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        void Refresh(object? sender, EventArgs e)
        {
            try
            {
                if (!WindowPickerCandidates.HasSameIdentity(handle, selected.ThreadId, selected.ProcessId))
                {
                    Close();
                    return;
                }
                // Recheck identity even while hidden, so a reused HWND never acquires the old outline.
                var current = WindowPickerCandidates.ReadCandidate(handle, 0);
                if (current is null) { Hide(); return; }
                if (current.ProcessId != selected.ProcessId || current.ThreadId != selected.ThreadId) { Close(); return; }
                if (!IsVisible || DesktopBounds != desktop)
                {
                    desktop = DesktopBounds;
                    ShowOverDesktop();
                }
                Update(current, 0, 0);
            }
            catch (Exception exception)
            {
                Close();
                logger.LogError(exception, "Failed to follow the selected window outline.");
            }
        }
        timer.Tick += Refresh;
        Closed += (_, _) => { timer.Stop(); timer.Tick -= Refresh; };
        timer.Start();
        Refresh(null, EventArgs.Empty);
    }

    public void Update(WindowPickerCandidate? candidate, int x, int y)
    {
        if (!IsVisible) return;
        _outline.Width = ActualWidth;
        _outline.Height = ActualHeight;
        _outline.Bounds = candidate is not null
            ? FromScreenPixels(new Rect(candidate.FrameBounds.X, candidate.FrameBounds.Y, candidate.FrameBounds.Width, candidate.FrameBounds.Height))
            : Rect.Empty;
        _outline.StrokeScale = (candidate?.Dpi ?? 96) / (96 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _outline.InvalidateVisual();
        if (_hint.Visibility != Visibility.Visible) return;
        var screen = WindowPickerCandidates.GetWorkAreaAt(x, y, DesktopBounds);
        var localScreen = FromScreenPixels(new Rect(screen.X, screen.Y, screen.Width, screen.Height));
        _hint.MaxWidth = Math.Max(1, localScreen.Width - 16);
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cursor = PointFromScreen(new Point(x, y));
        Canvas.SetLeft(_hint, Math.Clamp(cursor.X + 20, localScreen.Left + 8, Math.Max(localScreen.Left + 8, localScreen.Right - _hint.DesiredSize.Width - 8)));
        Canvas.SetTop(_hint, Math.Clamp(cursor.Y + 24, localScreen.Top + 8, Math.Max(localScreen.Top + 8, localScreen.Bottom - _hint.DesiredSize.Height - 8)));
    }
}
