using System.Windows;
using System.Windows.Input;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.App.Views.Support;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Support;

/// <summary>Limits automatic display to one startup check and owns the modeless support window.</summary>
internal sealed class SupportCoordinator : IDisposable
{
    private readonly SupportService _support;
    private readonly MainWindowViewModel _main;
    private readonly Window _owner;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private SupportWindow? _window;
    private bool _scheduled;
    private bool _disposed;

    public SupportCoordinator(SupportService support, MainWindowViewModel main, Window owner,
        IStringLocalizer localizer, ILogger logger)
    {
        _support = support;
        _main = main;
        _owner = owner;
        _localizer = localizer;
        _logger = logger;
        support.OpenRequested += OnOpen;
        support.MainViewReady += OnMainViewReady;
    }

    private async void OnMainViewReady(object? sender, EventArgs e)
    {
        if (_scheduled || _disposed) return;
        _scheduled = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token);
            if (_disposed) return;
            var canShow = _main.Onboarding.IsMain && _owner.IsVisible && _owner.WindowState != WindowState.Minimized
                && _window is null && _main.Selection?.IsSelectingWindow != true && !SupportWindows.ForegroundIsFullScreen();
            if (_support.TryMarkStartupPrompt(DateTime.Now, canShow)) Show(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Failed to display startup support invitation."); }
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        if (_disposed) return;
#if DEBUG
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            if (!_support.ResetForDebugging()) return;
            _window?.Close();
        }
#endif
        _support.CancelStartupReminder();
        Show(true);
    }

    private void Show(bool manually)
    {
        if (_window is not null)
        {
            if (manually) _window.ActivateCentered();
            return;
        }
        try
        {
            var window = new SupportWindow(_support, _localizer, _logger);
            _window = window;
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            window.ShowSupport(_owner, manually);
        }
        catch (Exception ex)
        {
            _window?.Close();
            _window = null;
            _logger.LogError(ex, "Failed to open support window.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _support.OpenRequested -= OnOpen;
        _support.MainViewReady -= OnMainViewReady;
        _window?.Close();
        _lifetime.Dispose();
    }
}
