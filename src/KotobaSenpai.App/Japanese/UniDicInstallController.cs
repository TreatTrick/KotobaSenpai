using KotobaSenpai.Core.Errors;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Platform.Windows.Japanese;

namespace KotobaSenpai.App.Japanese;

/// <summary>
/// Dictionary install coordinator: drives <see cref="UniDicDictionaryInstaller"/> to perform the install at startup,
/// exposing progress/error state for the main window's overlay to bind. During installation the overlay blocks all other operations; on failure it shows the error inside the overlay and allows retry.
/// <paramref name="install"/> can be injected (for tests) and otherwise delegates to the installer's <see cref="UniDicDictionaryInstaller.EnsureInstalledAsync"/>.
/// </summary>
public sealed partial class UniDicInstallController : ObservableObject, IDisposable
{
    private readonly IUserMessageResolver _messageResolver;
    private readonly UniDicDictionaryInstaller _installer;
    private readonly Func<CancellationToken, IProgress<double>, Task>? _install;
    private readonly IStringLocalizer? _localizer;
    private readonly Func<string?> _dictionaryOverride;
    private bool _disposed;

    public Exception? Failure { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StageText))]
    private DictionaryInstallStage _stage;
    [ObservableProperty] private bool _isIndeterminate = true;
    public string StageText => _localizer?.Get("Install_" + Stage) ?? Stage.ToString();

    [ObservableProperty]
    private double _progress;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking))]
    private bool _isInstalling;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking), nameof(HasError))]
    private string? _error;

    public event EventHandler? InstallationCompleted;

    [ObservableProperty]
    private bool _isInstalled;

    public UniDicInstallController(
        UniDicDictionaryInstaller installer,
        IUserMessageResolver messageResolver,
        Func<CancellationToken, IProgress<double>, Task>? install = null,
        IStringLocalizer? localizer = null,
        Func<string?>? dictionaryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(installer);
        _messageResolver = messageResolver ?? throw new ArgumentNullException(nameof(messageResolver));
        _installer = installer;
        _install = install;
        _localizer = localizer;
        _dictionaryOverride = dictionaryOverride ?? (() => Environment.GetEnvironmentVariable("KOTOBA_UNIDIC_DIR"));
        IsInstalled = CheckInstalled();
        if (IsInstalled) ReportStage(new(DictionaryInstallStage.Ready));
        if (_localizer is not null) _localizer.CultureChanged += OnCultureChanged;
    }

    public bool CheckInstalled()
    {
        if (_install is not null) return IsInstalled;
        try
        {
            var directory = _dictionaryOverride();
            if (string.IsNullOrWhiteSpace(directory)) return _installer.IsInstalled;
            UniDicTokenizer.ValidateDictionary(directory, requireInstalledManifest: false);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Overlay is visible (blocking operations): installing or an error is present.</summary>
    public bool IsBlocking => IsInstalling || HasError;

    /// <summary>There is a displayable error message after a failed install.</summary>
    public bool HasError => Error is not null;

    /// <summary>Performs the install; returns immediately when already installed. On success the overlay is dismissed; on failure a localized error is shown inside it.</summary>
    [RelayCommand(IncludeCancelCommand = true)]
    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (_disposed || IsInstalling) return;
        if (CheckInstalled())
        {
            IsInstalled = true;
            ReportStage(new(DictionaryInstallStage.Ready));
            return;
        }

        IsInstalling = true;
        Error = null;
        Failure = null;
        Progress = 0;
        IsInstalled = false;
        ReportStage(new(DictionaryInstallStage.Downloading));
        try
        {
            if (_install is not null)
                await _install(ct, new Progress<double>(p => { if (IsInstalling) Progress = p; }));
            else if (_dictionaryOverride() is { } directory && !string.IsNullOrWhiteSpace(directory))
                await Task.Run(() => UniDicTokenizer.ValidateDictionary(directory, requireInstalledManifest: false), ct);
            else
                await _installer.EnsureInstalledAsync(ct: ct,
                    stages: new Progress<DictionaryInstallProgress>(value =>
                    {
                        if (IsInstalling && !_disposed) ReportStage(value);
                    }));
            ct.ThrowIfCancellationRequested();
            if (_disposed) return;
            IsInstalled = true;
            ReportStage(new(DictionaryInstallStage.Ready));
            InstallationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: keep the overlay visible and let the user decide whether to retry or close the window.
        }
        catch (Exception ex)
        {
            Failure = ex;
            Error = _messageResolver.Resolve(ex, ErrorCodes.UniDicDownloadFailed);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void ReportStage(DictionaryInstallProgress value)
    {
        Stage = value.Stage;
        IsIndeterminate = value.Stage != DictionaryInstallStage.Ready && value.Fraction is null;
        Progress = value.Stage == DictionaryInstallStage.Ready ? 1 : value.Fraction ?? 0;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StageText));
        if (Failure is not null) Error = _messageResolver.Resolve(Failure, ErrorCodes.UniDicDownloadFailed);
    }

    public void Dispose()
    {
        _disposed = true;
        InstallCommand.Cancel();
        if (_localizer is not null) _localizer.CultureChanged -= OnCultureChanged;
    }
}
