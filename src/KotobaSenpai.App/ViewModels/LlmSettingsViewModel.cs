using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.App.Resources;

namespace KotobaSenpai.App.ViewModels;

public sealed partial class LlmProtocolChoice(string id, string name) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty]
    private string _name = name;
}

public sealed partial class LlmSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ILlmModelCatalog _catalog;
    private readonly IStringLocalizer _localizer;
    private CancellationTokenSource? _fetchCancellation;
    private string? _modelStatusKey;
    private object[] _modelStatusArguments = [];
    private bool _disposed;
    private readonly LlmConfigurationService? _configuration;
    private string? _configurationStatusKey;
    private string? _promptCopyStatusKey;
    private CancellationTokenSource? _configurationCancellation;
    private int _configurationRevision;
    private string? _validationErrorCode;
    private string? _validationErrorDetail;

    public LlmSettingsViewModel(ISettingsService settings, ILlmModelCatalog catalog, IStringLocalizer localizer,
        LlmConfigurationService? configuration = null)
    {
        _settings = settings;
        _catalog = catalog;
        _localizer = localizer;
        _configuration = configuration;
        _protocol = LlmProtocolIds.Normalize(settings.GetValue(LlmSettingsKeys.Protocol));
        _endpoint = settings.GetValue(LlmSettingsKeys.Endpoint) ?? string.Empty;
        _apiKey = settings.GetValue(LlmSettingsKeys.ApiKey) ?? string.Empty;
        _model = settings.GetValue(LlmSettingsKeys.Model) ?? string.Empty;
        RefreshProtocolChoices();
        IsConfigurationUsable = _configuration?.GetStatus().Verified == true;
        _localizer.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<LlmModelInfo> Models { get; } = [];
    public ObservableCollection<LlmProtocolChoice> Protocols { get; } = [];

    [ObservableProperty]
    private string _protocol = LlmProtocolIds.OpenAiChat;

    [ObservableProperty]
    private string _endpoint = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private bool _isFetchingModels;

    [ObservableProperty]
    private bool _isApiKeyVisible;

    [ObservableProperty]
    private string _modelStatus = string.Empty;

    [ObservableProperty]
    private string _configurationStatus = string.Empty;

    [ObservableProperty]
    private string _promptCopyStatus = string.Empty;

    [ObservableProperty]
    private bool _isPromptCopied;

    [ObservableProperty]
    private bool _isCheckingConfiguration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationMessage))]
    private bool _isConfigurationUsable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationMessage))]
    private bool _isConfigurationUnavailable;

    [ObservableProperty]
    private string _configurationErrorDetails = string.Empty;

    public string ConfigurationValidationMessage => IsConfigurationUsable ? _localizer.Get("Agent_ConfigurationUsable")
        : IsConfigurationUnavailable ? _localizer.Get("Agent_ConfigurationUnavailable") : string.Empty;

    partial void OnProtocolChanged(string value)
    {
        SaveField(LlmSettingsKeys.Protocol, value);
        CancelStaleFetch();
    }

    partial void OnEndpointChanged(string value)
    {
        SaveField(LlmSettingsKeys.Endpoint, value);
        CancelStaleFetch();
    }

    partial void OnApiKeyChanged(string value)
    {
        SaveField(LlmSettingsKeys.ApiKey, value);
        CancelStaleFetch();
    }
    partial void OnModelChanged(string value) => SaveField(LlmSettingsKeys.Model, value);

    private void SaveField(string key, string value)
    {
        _settings.SetValue(key, value);
        _configurationRevision++;
        _configurationCancellation?.Cancel();
        SetValidation(null);
        SetConfigurationStatus(null);
    }

    public string? CreateAgentPrompt(string? cliPath)
    {
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            SetPromptCopyStatus("Agent_ToolMissing");
            return null;
        }
        return AgentPromptBuilder.Build(_localizer, cliPath);
    }

    public void ReportPromptCopy(bool succeeded) => SetPromptCopyStatus(succeeded ? "Agent_PromptCopied" : "Agent_CopyFailed", succeeded);
    public void ReportErrorCopy(bool succeeded) => SetConfigurationStatus(succeeded ? "Agent_ErrorCopied" : "Agent_ErrorCopyFailed");

    [RelayCommand]
    private void CancelConfiguration() => _configurationCancellation?.Cancel();

    [RelayCommand]
    private async Task CheckConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_configuration is null || IsCheckingConfiguration)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _configurationCancellation = linked;
        IsCheckingConfiguration = true;
        var revision = _configurationRevision;
        SetValidation(null);
        SetConfigurationStatus("Agent_Checking");
        try
        {
            var result = await _configuration.CheckCurrentAsync(linked.Token);
            if (_disposed || revision != _configurationRevision)
                return;
            SetConfigurationStatus(result.Code == "cancelled" ? ResourceKeys.Llm_FetchCancelled : null);
            if (result.Code == "ok")
                SetValidation(true);
            else if (result.Code != "cancelled")
                SetValidation(false, result.Code, result.ErrorDetail);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && revision == _configurationRevision)
                SetConfigurationStatus(ResourceKeys.Llm_FetchCancelled);
        }
        catch (Exception)
        {
            if (!_disposed && revision == _configurationRevision)
            {
                SetConfigurationStatus(null);
                SetValidation(false, "check_failed");
            }
        }
        finally
        {
            _configurationCancellation = null;
            if (!_disposed)
            {
                IsCheckingConfiguration = false;
            }
        }
    }

    private void SetConfigurationStatus(string? key)
    {
        _configurationStatusKey = key;
        ConfigurationStatus = key is null ? string.Empty : _localizer.Get(key);
    }

    private void SetPromptCopyStatus(string key, bool copied = false)
    {
        _promptCopyStatusKey = key;
        PromptCopyStatus = _localizer.Get(key);
        IsPromptCopied = copied;
    }

    private void SetValidation(bool? usable, string? errorCode = null, string? detail = null)
    {
        IsConfigurationUsable = usable == true;
        IsConfigurationUnavailable = usable == false;
        _validationErrorCode = errorCode;
        _validationErrorDetail = detail;
        RefreshValidationError();
    }

    private void RefreshValidationError()
    {
        if (_validationErrorCode is not { } code)
        {
            ConfigurationErrorDetails = string.Empty;
            return;
        }
        var key = code switch
        {
            "missing_api_key" => ResourceKeys.Llm_ApiKeyRequired,
            "missing_endpoint" or "invalid_endpoint" => ResourceKeys.Llm_EndpointRequired,
            "missing_model" => "Agent_ModelRequired",
            "authentication_failed" => ResourceKeys.Llm_AuthenticationFailed,
            "timeout" => ResourceKeys.Llm_Timeout,
            "transport_error" => "Agent_ConnectionFailed",
            "rate_limited" => "Agent_RateLimited",
            "provider_unavailable" => "Agent_ProviderUnavailable",
            "conflict" => "Agent_Conflict",
            "check_failed" => "Agent_CheckFailed",
            _ => "Agent_ValidationFailed",
        };
        var text = $"{_localizer.Get(key)} [{code}]";
        if (!string.IsNullOrWhiteSpace(_validationErrorDetail))
            text += Environment.NewLine + _validationErrorDetail;
        ConfigurationErrorDetails = string.IsNullOrEmpty(ApiKey) ? text
            : text.Replace(ApiKey, "[REDACTED]", StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(ApiKey), "[REDACTED]", StringComparison.Ordinal);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task FetchModelsAsync()
    {
        _fetchCancellation?.Cancel();
        _fetchCancellation?.Dispose();
        _fetchCancellation = null;
        IsFetchingModels = false;
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            SetModelStatus(ResourceKeys.Llm_EndpointRequired);
            return;
        }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            SetModelStatus(ResourceKeys.Llm_ApiKeyRequired);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _fetchCancellation = cancellation;
        IsFetchingModels = true;
        SetModelStatus(ResourceKeys.Llm_LoadingModels);
        try
        {
            var result = await _catalog.GetModelsAsync(new LlmModelCatalogRequest(Endpoint, ApiKey, Protocol), cancellation.Token);
            if (!ReferenceEquals(_fetchCancellation, cancellation))
                return;
            if (result.Succeeded)
            {
                Models.Clear();
                foreach (var entry in result.Models)
                    Models.Add(entry);
                if (Models.Count == 0)
                    SetModelStatus(ResourceKeys.Llm_NoModels);
                else
                    SetModelStatus(ResourceKeys.Llm_ModelsLoaded, Models.Count);
            }
            else
            {
                SetModelStatus(result.Failure switch
                {
                    LlmModelCatalogFailure.Authentication => ResourceKeys.Llm_AuthenticationFailed,
                    LlmModelCatalogFailure.Timeout => ResourceKeys.Llm_Timeout,
                    LlmModelCatalogFailure.Unsupported or LlmModelCatalogFailure.NotFound => ResourceKeys.Llm_CatalogUnavailable,
                    LlmModelCatalogFailure.Empty => ResourceKeys.Llm_NoModels,
                    LlmModelCatalogFailure.MissingEndpoint => ResourceKeys.Llm_EndpointRequired,
                    LlmModelCatalogFailure.MissingApiKey => ResourceKeys.Llm_ApiKeyRequired,
                    LlmModelCatalogFailure.Cancelled => ResourceKeys.Llm_FetchCancelled,
                    _ => ResourceKeys.Llm_FetchFailed,
                });
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            if (ReferenceEquals(_fetchCancellation, cancellation))
                SetModelStatus(ResourceKeys.Llm_FetchFailed);
        }
        finally
        {
            if (ReferenceEquals(_fetchCancellation, cancellation))
            {
                IsFetchingModels = false;
                _fetchCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

    private void RefreshProtocolChoices()
    {
        foreach (var option in LlmProtocolIds.Options)
        {
            var name = _localizer.Get(option.DisplayNameKey);
            if (Protocols.FirstOrDefault(choice => choice.Id == option.Id) is { } existing)
                existing.Name = name;
            else
                Protocols.Add(new LlmProtocolChoice(option.Id, name));
        }
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshProtocolChoices();
        if (_modelStatusKey is not null)
            ModelStatus = _localizer.Get(_modelStatusKey, _modelStatusArguments);
        if (_configurationStatusKey is not null)
            ConfigurationStatus = _localizer.Get(_configurationStatusKey);
        if (_promptCopyStatusKey is not null)
            PromptCopyStatus = _localizer.Get(_promptCopyStatusKey);
        OnPropertyChanged(nameof(ConfigurationValidationMessage));
        RefreshValidationError();
    }

    private void SetModelStatus(string key, params object[] args)
    {
        _modelStatusKey = key;
        _modelStatusArguments = args;
        ModelStatus = _localizer.Get(key, args);
    }

    private void CancelStaleFetch()
    {
        if (_fetchCancellation is not { } cancellation)
            return;
        _fetchCancellation = null;
        cancellation.Cancel();
        IsFetchingModels = false;
        _modelStatusKey = null;
        ModelStatus = string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
        _configurationCancellation?.Cancel();
        _fetchCancellation?.Cancel();
        _fetchCancellation?.Dispose();
    }
}
