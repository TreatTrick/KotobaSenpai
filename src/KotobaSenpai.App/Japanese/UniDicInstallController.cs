using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Japanese;

/// <summary>
/// Dictionary install coordinator: drives <see cref="UniDicDictionaryInstaller"/> to perform the install at startup,
/// exposing progress/error state for the main window's overlay to bind. During installation the overlay blocks all other operations; on failure it shows the error inside the overlay and allows retry.
/// <paramref name="install"/> can be injected (for tests) and otherwise delegates to the installer's <see cref="UniDicDictionaryInstaller.EnsureInstalledAsync"/>.
/// </summary>
public sealed partial class UniDicInstallController : ObservableObject
{
    private readonly IUserMessageResolver _messageResolver;
    private readonly Func<CancellationToken, IProgress<double>, Task> _install;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking))]
    private bool _isInstalling;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking), nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _isInstalled;

    public UniDicInstallController(
        UniDicDictionaryInstaller installer,
        IUserMessageResolver messageResolver,
        Func<CancellationToken, IProgress<double>, Task>? install = null)
    {
        ArgumentNullException.ThrowIfNull(installer);
        _messageResolver = messageResolver ?? throw new ArgumentNullException(nameof(messageResolver));
        _install = install ?? ((ct, p) => installer.EnsureInstalledAsync(p, ct));
        IsInstalled = installer.IsInstalled;
    }

    /// <summary>Overlay is visible (blocking operations): installing or an error is present.</summary>
    public bool IsBlocking => IsInstalling || HasError;

    /// <summary>There is a displayable error message after a failed install.</summary>
    public bool HasError => Error is not null;

    /// <summary>Performs the install; returns immediately when already installed. On success the overlay is dismissed; on failure a localized error is shown inside it.</summary>
    [RelayCommand]
    private async Task InstallAsync(CancellationToken ct = default)
    {
        if (IsInstalled)
            return;

        IsInstalling = true;
        Error = null;
        Progress = 0;
        try
        {
            await _install(ct, new Progress<double>(p => Progress = p));
            IsInstalled = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled: keep the overlay visible and let the user decide whether to retry or close the window.
        }
        catch (Exception ex)
        {
            Error = _messageResolver.Resolve(ex, ErrorCodes.UniDicDownloadFailed);
        }
        finally
        {
            IsInstalling = false;
        }
    }
}