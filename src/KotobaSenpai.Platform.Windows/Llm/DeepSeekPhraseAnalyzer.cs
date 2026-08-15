using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// <see cref="ILlmPhraseAnalyzer"/> 的协议无关适配器。BYOK 设置经 <see cref="ISettingsService"/> 读取；
/// 传输、Bearer 鉴权、错误映射、取消/超时在此，请求/响应信封交给所选 <see cref="ILlmProtocol"/>。
/// 未配置 key 时跳过调用。取消/超时/传输/拒绝/畸形 JSON 均映射为可重试诊断，不抛穿识别流程。
/// </summary>
public sealed class DeepSeekPhraseAnalyzer : ILlmPhraseAnalyzer
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly ILlmProtocol _protocol;
    private readonly PhrasePromptBuilder _promptBuilder;
    private readonly PhraseResponseParser _parser;

    public DeepSeekPhraseAnalyzer(
        ISettingsService settings,
        HttpClient httpClient,
        ILlmProtocol? protocol = null,
        PhrasePromptBuilder? promptBuilder = null,
        PhraseResponseParser? parser = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _protocol = protocol ?? new OpenAiChatCompletionsProtocol();
        _promptBuilder = promptBuilder ?? new PhrasePromptBuilder();
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

        string systemPrompt, userContent;
        try
        {
            (systemPrompt, userContent) = _promptBuilder.Build(request);
        }
        catch (RequestTooLargeException ex)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.InvalidResponse, [], ex.Message);
        }

        var body = _protocol.BuildBody(systemPrompt, userContent, model);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + _protocol.Path);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
            var groups = _parser.ParseGroups(_protocol.ExtractGroupsJson(envelope));
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, groups, null);
        }
        catch (Exception ex) when (ex is PhraseResponseException or JsonException or KeyNotFoundException or ArgumentNullException)
        {
            // 协议信封或 group 结构不符（缺字段/文本为 null/非 JSON）→ 可重试警告，不抛穿识别流程。
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.MalformedJson, [], ex.Message);
        }
    }
}