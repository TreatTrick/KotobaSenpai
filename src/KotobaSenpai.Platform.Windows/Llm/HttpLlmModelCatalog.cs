using System.Net;
using System.Net.Http;
using System.Text.Json;
using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>HTTP model catalog adapter with bounded candidates and tolerant OpenAI-compatible parsing.</summary>
public sealed class HttpLlmModelCatalog : ILlmModelCatalog
{
    private static readonly TimeSpan CandidateTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _http;
    private readonly LlmProtocolRegistry _registry;

    public HttpLlmModelCatalog(HttpClient http, LlmProtocolRegistry registry)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.MissingEndpoint);
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.MissingApiKey);

        var protocol = _registry.Resolve(request.Protocol);
        var candidates = LlmEndpointHelper.ModelCandidates(request.Endpoint);
        if (candidates.Count == 0)
            return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Transport);

        var sawNotFound = false;
        foreach (var candidate in candidates)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CandidateTimeout);
            using var message = new HttpRequestMessage(HttpMethod.Get, candidate);
            try
            {
                protocol.ApplyAuthentication(message, request.ApiKey);
                using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                {
                    sawNotFound = true;
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Authentication);
                if (!response.IsSuccessStatusCode)
                    return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Transport, $"HTTP {(int)response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                return Parse(json);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Cancelled);
            }
            catch (OperationCanceledException)
            {
                return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Timeout);
            }
            catch (HttpRequestException)
            {
                return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Transport);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Transport);
            }
        }

        return LlmModelCatalogResult.Failed(sawNotFound ? LlmModelCatalogFailure.NotFound : LlmModelCatalogFailure.Unsupported);
    }

    private static LlmModelCatalogResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var models = new Dictionary<string, LlmModelInfo>(StringComparer.Ordinal);
            var hasDataArray = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("data", out var dataArray)
                && dataArray.ValueKind == JsonValueKind.Array;
            var hasModelsArray = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("models", out var modelsArray)
                && modelsArray.ValueKind == JsonValueKind.Array;
            if (!hasDataArray && !hasModelsArray)
                return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Unsupported);
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idElement))
                        continue;
                    Add(models, ReadString(idElement), item.TryGetProperty("owned_by", out var owner) ? ReadString(owner) : null);
                }
            }
            if (models.Count == 0 && root.TryGetProperty("models", out var alternate) && alternate.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in alternate.EnumerateArray())
                {
                    var slug = item.TryGetProperty("slug", out var slugElement) ? ReadString(slugElement) : null;
                    var id = item.TryGetProperty("id", out var idElement) ? ReadString(idElement) : null;
                    Add(models, string.IsNullOrWhiteSpace(slug) ? id : slug, null);
                }
            }
            return models.Count == 0
                ? LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Empty)
                : LlmModelCatalogResult.Success(models.Values.OrderBy(model => model.Id, StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Unsupported, ex.Message);
        }
    }

    private static string? ReadString(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void Add(IDictionary<string, LlmModelInfo> models, string? id, string? owner)
    {
        id = id?.Trim();
        if (string.IsNullOrEmpty(id))
            return;
        owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
        if (models.TryGetValue(id, out var existing))
        {
            if (existing.Owner is null && owner is not null)
                models[id] = existing with { Owner = owner };
            return;
        }
        models[id] = new LlmModelInfo(id, owner);
    }
}
