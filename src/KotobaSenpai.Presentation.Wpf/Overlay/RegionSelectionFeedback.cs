using System.Windows;
using System.Windows.Threading;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Owns the five-pulse passive feedback lifetime independently of interactive region editing.</summary>
internal sealed class RegionSelectionFeedback : IDisposable
{
    private readonly FrameworkElement _outline;
    private readonly DispatcherTimer _timer;
    private int _remaining;
    public bool IsActive { get; private set; }
    public event EventHandler? Completed;

    public RegionSelectionFeedback(FrameworkElement outline, Dispatcher dispatcher)
    {
        _outline = outline;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
    }

    public void Activate()
    {
        IsActive = true;
        _remaining = 5;
        _outline.Visibility = Visibility.Visible;
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        IsActive = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_outline.Visibility == Visibility.Visible)
        {
            _outline.Visibility = Visibility.Hidden;
            if (--_remaining == 0) { _timer.Stop(); Completed?.Invoke(this, EventArgs.Empty); }
        }
        else _outline.Visibility = Visibility.Visible;
    }

    public void Dispose() { Stop(); _timer.Tick -= OnTick; }
}
