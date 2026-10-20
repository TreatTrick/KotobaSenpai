using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.App.Resources;

namespace KotobaSenpai.App.ViewModels;

public sealed record LlmProtocolChoice(string Id, string Name);

public sealed partial class LlmSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ILlmModelCatalog _catalog;
    private readonly IStringLocalizer _localizer;
    private CancellationTokenSource? _fetchCancellation;
    private string? _modelStatusKey;
    private object[] _modelStatusArguments = [];
    private bool _disposed;

    public LlmSettingsViewModel(ISettingsService settings, ILlmModelCatalog catalog, IStringLocalizer localizer)
    {
        _settings = settings;
        _catalog = catalog;
        _localizer = localizer;
        _enabled = IsEnabled(settings.GetValue(LlmSettingsKeys.Enabled));
        _protocol = LlmProtocolIds.Normalize(settings.GetValue(LlmSettingsKeys.Protocol));
        _endpoint = settings.GetValue(LlmSettingsKeys.Endpoint) ?? string.Empty;
        _apiKey = settings.GetValue(LlmSettingsKeys.ApiKey) ?? string.Empty;
        _model = settings.GetValue(LlmSettingsKeys.Model) ?? string.Empty;
        RefreshProtocolChoices();
        _localizer.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<LlmModelInfo> Models { get; } = [];
    public ObservableCollection<LlmProtocolChoice> Protocols { get; } = [];

    [ObservableProperty]
    private bool _enabled;

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

    partial void OnEnabledChanged(bool value) => _settings.SetValue(LlmSettingsKeys.Enabled, value ? "true" : "false");
    partial void OnProtocolChanged(string value)
    {
        _settings.SetValue(LlmSettingsKeys.Protocol, value);
        CancelStaleFetch();
    }

    partial void OnEndpointChanged(string value)
    {
        _settings.SetValue(LlmSettingsKeys.Endpoint, value);
        CancelStaleFetch();
    }

    partial void OnApiKeyChanged(string value)
    {
        _settings.SetValue(LlmSettingsKeys.ApiKey, value);
        CancelStaleFetch();
    }
    partial void OnModelChanged(string value) => _settings.SetValue(LlmSettingsKeys.Model, value);

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
        Protocols.Clear();
        foreach (var option in LlmProtocolIds.Options)
            Protocols.Add(new LlmProtocolChoice(option.Id, _localizer.Get(option.DisplayNameKey)));
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshProtocolChoices();
        if (_modelStatusKey is not null)
            ModelStatus = _localizer.Get(_modelStatusKey, _modelStatusArguments);
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

    private static bool IsEnabled(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
        _fetchCancellation?.Cancel();
        _fetchCancellation?.Dispose();
    }
}
