using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KotobaSenpai.App.Controls;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Views;

/// <summary>Displays the recognition page's state and commands.</summary>
public partial class RecognitionPage : UserControl
{
    private RecognitionPageViewModel? _viewModel;
    private Window? _host;
    private WindowState _savedState;
    private bool _hiddenForPicker;
    private bool _hostClosed;
    private CancellationTokenSource? _flight;

    public RecognitionPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveViewModel();
        Loaded += (_, _) => ObserveViewModel();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && _viewModel?.IsPickingWindow != true) CancelFlight();
        };
        Unloaded += (_, _) =>
        {
            if (_hiddenForPicker) return;
            _viewModel?.CancelWindowSelection();
            CancelFlight();
        };
    }

    private void ObserveViewModel()
    {
        if (_viewModel == DataContext) return;
        if (_viewModel is not null)
        {
            _viewModel.CancelWindowSelection();
            RestoreHost();
            _viewModel.PropertyChanged -= OnSelectionChanged;
            _viewModel.PreviewReady -= OnPreviewReady;
        }
        CancelFlight();
        TargetPreviewImage.Source = null;
        _viewModel = DataContext as RecognitionPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnSelectionChanged;
            _viewModel.PreviewReady += OnPreviewReady;
        }
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.PropertyName == nameof(RecognitionPageViewModel.SelectionVersion))
        {
            CancelFlight();
            TargetPreviewImage.Source = null;
        }
        if (e.PropertyName == nameof(RecognitionPageViewModel.IsPickingWindow))
        {
            if (_viewModel.IsPickingWindow) HideHost();
            else RestoreHost();
        }
    }

    private void HideHost()
    {
        CancelFlight();
        if (_host is null)
        {
            _host = Window.GetWindow(this);
            if (_host is not null) _host.Closed += OnHostClosed;
        }
        if (_host?.IsVisible != true) return;
        _savedState = _host.WindowState;
        _hiddenForPicker = true;
        _host.Hide();
    }

    private void RestoreHost()
    {
        if (!_hiddenForPicker || _hostClosed || _host is null) return;
        _hiddenForPicker = false;
        _host.WindowState = _savedState;
        _host.Show();
        _host.Activate();
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        _hostClosed = true;
        CancelFlight();
        _viewModel?.CancelWindowSelection();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnSelectionChanged;
            _viewModel.PreviewReady -= OnPreviewReady;
        }
        TargetPreviewImage.Source = null;
    }

    private void OnPreviewReady(object? sender, WindowPreviewEventArgs preview)
    {
        if (_viewModel?.SelectionVersion != preview.Version || _hostClosed) return;
        CancelFlight();
        var frame = preview.Frame;
        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null,
            frame.Bgra32.ToArray(), checked(frame.Width * 4));
        source.Freeze();
        TargetPreviewImage.Source = WindowPreviewFlight.CreateThumbnail(source,
            (int)Math.Ceiling(144 * VisualTreeHelper.GetDpi(this).DpiScaleX));
        if (!preview.Animate || !SystemParameters.ClientAreaAnimation) return;
        _flight = new CancellationTokenSource();
        _ = PlayFlightAsync(source, preview, _flight);
    }

    private async Task PlayFlightAsync(BitmapSource source, WindowPreviewEventArgs preview, CancellationTokenSource cancellation)
    {
        try
        {
            TargetPreviewImage.Opacity = 0;
            TargetWindowCard.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (cancellation.IsCancellationRequested || !IsVisible || _host?.IsActive != true) return;
            UpdateLayout();
            await WindowPreviewFlight.PlayAsync(source, preview.Bounds, TargetPreviewImage, cancellation.Token);
            if (!cancellation.IsCancellationRequested && _viewModel?.SelectionVersion == preview.Version)
            {
                var transform = (ScaleTransform)TargetPreviewImage.RenderTransform;
                var settle = new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(120));
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            }
        }
        catch (Exception exception) { _viewModel?.ReportPresentationFailure(exception); }
        finally
        {
            if (ReferenceEquals(_flight, cancellation))
            {
                TargetPreviewImage.Opacity = 1;
                _flight = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelFlight()
    {
        _flight?.Cancel();
        TargetPreviewImage.Opacity = 1;
        var transform = (ScaleTransform)TargetPreviewImage.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }
}
