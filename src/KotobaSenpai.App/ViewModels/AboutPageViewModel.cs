using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Updates;

namespace KotobaSenpai.App.ViewModels;

/// <summary>Owns update presentation without knowing about HTTP, files, WPF or native installers.</summary>
public sealed partial class AboutPageViewModel : ObservableObject, IDisposable
{
    private readonly IAppUpdateSource _source;
    private readonly IAppUpdateInstaller _installer;
    private readonly IStringLocalizer _localizer;
    private readonly IUserMessageResolver _messages;
    private readonly ILogger _logger;
    private readonly Version _currentVersion;
    private AppRelease? _release;
    private DownloadedAppUpdate? _download;
    private Exception? _failure;
    private string _fallbackCode = ErrorCodes.AppUpdateCheckFailed;
    private string _statusKey = ResourceKeys.About_NotChecked;
    private long _received;
    private bool _hasChecked;
    private bool _installerStarted;
    private bool _disposed;

    public AboutPageViewModel(IAppUpdateSource source, IAppUpdateInstaller installer, IStringLocalizer localizer,
        IUserMessageResolver messages, ILogger logger, Version currentVersion)
    {
        _source = source;
        _installer = installer;
        _localizer = localizer;
        _messages = messages;
        _logger = logger;
        _currentVersion = new(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        CanAutoInstall = installer.IsAvailable;
        _localizer.CultureChanged += OnCultureChanged;
    }

    public event EventHandler? InstallStarted;
    public string CurrentVersion => _currentVersion.ToString(3);
    public string CurrentVersionText => _localizer.Get(ResourceKeys.About_CurrentVersion, CurrentVersion);
    public string LatestVersionText => _localizer.Get(ResourceKeys.About_LatestVersion, _release?.Version.ToString(3) ?? string.Empty);
    public string ReleaseNotes => string.IsNullOrWhiteSpace(_release?.ReleaseNotes)
        ? _localizer.Get(ResourceKeys.About_NoReleaseNotes) : _release.ReleaseNotes;
    public string PackageSize => _localizer.Get(ResourceKeys.About_PackageSize, (_release?.Size ?? 0) / 1048576d);
    public string ProgressText => _localizer.Get(ResourceKeys.About_DownloadProgress, _received / 1048576d, (_release?.Size ?? 0) / 1048576d);
    public string Status => _failure is null ? _localizer.Get(_statusKey) : _messages.Resolve(_failure, _fallbackCode);
    public bool HasUpdate => _release is not null;
    public bool ShowDownload => HasUpdate && !IsReady;
    public bool CanAutoInstall { get; }
    public bool RequiresManualInstall => !CanAutoInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand), nameof(InstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownload))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(InstallCommand))]
    private bool _isReady;

    public Task EnsureCheckedAsync() => _hasChecked || !CanCheck() ? Task.CompletedTask : CheckCommand.ExecuteAsync(null);

    private bool CanCheck() => !_disposed && !IsBusy;
    private bool CanDownload() => CanCheck() && HasUpdate && !IsReady;
    private bool CanInstall() => CanCheck() && IsReady && CanAutoInstall;
    private bool CanCancelDownload() => !_disposed && IsDownloading;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload() => DownloadCommand.Cancel();

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!CanCheck()) return;
        _hasChecked = true;
        IsBusy = true;
        _release = null;
        _download = null;
        IsReady = false;
        SetStatus(ResourceKeys.About_Checking);
        try
        {
            var release = await _source.CheckAsync(_currentVersion, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            _release = release;
            SetStatus(release is null ? ResourceKeys.About_UpToDate : ResourceKeys.About_UpdateAvailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { SetFailure(exception, ErrorCodes.AppUpdateCheckFailed); }
        finally
        {
            IsBusy = false;
            RefreshText();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        if (!CanDownload()) return;
        IsBusy = true;
        IsDownloading = true;
        _received = 0;
        DownloadProgress = 0;
        var reporting = true;
        SetStatus(ResourceKeys.About_Downloading);
        var progress = new Progress<AppUpdateProgress>(value =>
        {
            if (!reporting || _disposed || cancellationToken.IsCancellationRequested) return;
            _received = value.BytesReceived;
            DownloadProgress = value.TotalBytes <= 0 ? 0 : Math.Clamp(100d * value.BytesReceived / value.TotalBytes, 0, 100);
            OnPropertyChanged(nameof(ProgressText));
            if (value.IsVerifying) SetStatus(ResourceKeys.About_Verifying);
        });
        try
        {
            var download = await _source.DownloadAsync(_release!, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            _download = download;
            IsReady = true;
            SetStatus(ResourceKeys.About_Ready);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(ResourceKeys.About_Cancelled);
        }
        catch (Exception exception) { SetFailure(exception, ErrorCodes.AppUpdateDownloadFailed); }
        finally
        {
            reporting = false;
            IsDownloading = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (!CanInstall()) return;
        IsBusy = true;
        SetStatus(ResourceKeys.About_StartingInstaller);
        try
        {
            await _installer.LaunchAsync(_download!, cancellationToken);
            // Once handoff succeeds the installer is waiting for this process; do not strand it by accepting another action.
            _installerStarted = true;
            InstallStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _download = null;
            IsReady = false;
            SetFailure(exception, ErrorCodes.AppUpdateInstallFailed);
        }
        finally { if (!_installerStarted) IsBusy = false; }
    }

    public void ReportLinkFailure(Exception exception) => SetFailure(exception, ResourceKeys.About_LinkFailed);

    private void SetStatus(string key)
    {
        _failure = null;
        _statusKey = key;
        RefreshText();
    }

    private void SetFailure(Exception exception, string fallbackCode)
    {
        if (_disposed) return;
        _failure = exception;
        _fallbackCode = fallbackCode;
        _logger.LogError(exception, "Application update operation failed ({0}).", fallbackCode);
        OnPropertyChanged(nameof(Status));
    }

    private void OnCultureChanged(object? sender, EventArgs e) => RefreshText();

    private void RefreshText()
    {
        foreach (var property in new[] { nameof(CurrentVersionText), nameof(LatestVersionText), nameof(ReleaseNotes),
            nameof(PackageSize), nameof(ProgressText), nameof(Status), nameof(HasUpdate), nameof(ShowDownload) })
            OnPropertyChanged(property);
        DownloadCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
        CheckCommand.Cancel();
        DownloadCommand.Cancel();
        InstallCommand.Cancel();
        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
    }
}
