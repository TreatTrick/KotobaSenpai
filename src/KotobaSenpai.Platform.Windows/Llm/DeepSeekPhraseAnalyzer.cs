using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// <see cref="ILlmPhraseAnalyzer"/> 的 DeepSeek 兼容适配器。BYOK 设置经 <see cref="ISettingsService"/>
/// 读取；未配置 key 时跳过调用。取消/超时/传输/拒绝/畸形 JSON 均映射为可重试诊断，不抛穿识别流程。
/// </summary>
public sealed class DeepSeekPhraseAnalyzer : ILlmPhraseAnalyzer
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly PhraseRequestBuilder _builder;
    private readonly PhraseResponseParser _parser;

    public DeepSeekPhraseAnalyzer(
        ISettingsService settings,
        HttpClient httpClient,
        PhraseRequestBuilder? builder = null,
        PhraseResponseParser? parser = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _builder = builder ?? new PhraseRequestBuilder();
        _parser = parser ?? new PhraseResponseParser();
    }

    public async Task<PhraseAnalysisResult> AnalyzeAsync(
        PhraseAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.GetValue(DeepSeekSettingsKeys.ApiKey);
        var endpoint = _settings.GetValue(DeepSeekSettingsKeys.Endpoint);
        var model = _settings.GetValue(DeepSeekSettingsKeys.Model);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.NoKey, [], null);

        string body;
        try
        {
            body = _builder.BuildBody(request, model);
        }
        catch (RequestTooLargeException ex)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.InvalidResponse, [], ex.Message);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.GetValue(DeepSeekSettingsKeys.ApiKey));
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        string envelope;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Refused, [], $"Provider returned {(int)response.StatusCode}.");
            if (!response.IsSuccessStatusCode)
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Refused, [], $"Provider refused with {(int)response.StatusCode}.");
            envelope = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.Timeout, [], null);
        }
        catch (OperationCanceledException)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.Cancelled, [], null);
        }
        catch (HttpRequestException ex)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.TransportError, [], ex.Message);
        }

        try
        {
            var groups = _parser.Parse(envelope);
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, groups, null);
        }
        catch (PhraseResponseException ex)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.MalformedJson, [], ex.Message);
        }
    }
}