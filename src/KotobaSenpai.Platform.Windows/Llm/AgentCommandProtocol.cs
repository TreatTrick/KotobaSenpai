using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Bounded JSON input, capability discovery and safe output for the CLI.</summary>
public static class AgentCommandProtocol
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    public const int MaximumInputCharacters = 64 * 1024;

    public static async Task<(AgentApiRequest? Request, AgentApiResult? Error)> ReadAsync(
        TextReader reader, string command, CancellationToken cancellationToken)
    {
        try
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                text.Append(buffer, 0, count);
                if (text.Length > MaximumInputCharacters)
                    return (null, AgentApiResult.Failure("input_too_large", 2));
            }
            var request = JsonSerializer.Deserialize<AgentApiRequest>(text.ToString(), JsonOptions);
            return request is null ? (null, AgentApiResult.Failure("invalid_json", 2))
                : (request, AgentApiContract.Validate(command, request));
        }
        catch (JsonException) { return (null, AgentApiResult.Failure("invalid_json", 2)); }
        catch (OperationCanceledException) { return (null, AgentApiResult.Failure("input_cancelled", 130)); }
        catch (IOException) { return (null, AgentApiResult.Failure("input_failed", 5)); }
    }

    public static string Serialize(object result, string? secret = null)
    {
        var node = JsonSerializer.SerializeToNode(result, JsonOptions);
        return Redact(node, secret)?.ToJsonString(JsonOptions) ?? "null";
    }

    private static JsonNode? Redact(JsonNode? node, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return node;
        if (node is JsonObject obj)
            foreach (var key in obj.Select(entry => entry.Key).ToArray())
                obj[key] = Redact(obj[key]?.DeepClone(), secret);
        else if (node is JsonArray array)
            for (var index = 0; index < array.Count; index++)
                array[index] = Redact(array[index]?.DeepClone(), secret);
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return JsonValue.Create(text.Replace(secret, "[REDACTED]", StringComparison.Ordinal));
        return node;
    }

    public static object Capabilities(string installationDirectory, IReadOnlyList<string> protocols) => new
    {
        schemaVersion = AgentApiContract.SchemaVersion, status = "success", code = "ok", exitCode = 0,
        operationId = Guid.NewGuid(),
        installationDirectory, protocols,
        commands = new[]
        {
            new { command = "capabilities --json", requiredFields = Array.Empty<string>(), writesSettings = false },
            new { command = "llm models --stdin --json", requiredFields = new[] { "schemaVersion", "endpoint", "apiKey" }, writesSettings = false },
            new { command = "llm detect --stdin --json", requiredFields = new[] { "schemaVersion", "endpoint", "apiKey", "model" }, writesSettings = false },
            new { command = "llm configure --stdin --json", requiredFields = new[] { "schemaVersion", "endpoint", "apiKey", "model" }, writesSettings = true },
            new { command = "llm status --json", requiredFields = Array.Empty<string>(), writesSettings = false },
        },
        inputFields = new Dictionary<string, object>
        {
            ["schemaVersion"] = new { type = "integer", value = AgentApiContract.SchemaVersion },
            ["endpoint"] = new { type = "string", description = "API URL, not a documentation URL. Base URLs and full endpoints are accepted." },
            ["apiKey"] = new { type = "string", description = "User-provided API key. Never copy credentials from other tools." },
            ["model"] = new { type = "string", description = "The model chosen by the user or Agent. Discover models first when needed." },
            ["language"] = new { type = "string", description = "zh-CN or en; optional." },
            ["websiteUrl"] = new { type = "string", description = "Optional reference URL; the Agent reads documentation, the tool does not." },
            ["protocolHint"] = new { type = "string", description = "Optional hint; it does not override protocol priority or verification." },
        },
        inputExample = new { schemaVersion = 1, endpoint = "https://api.example.com/v1", apiKey = "YOUR_API_KEY", model = "selected-model-id", language = "en" },
        exitCodes = new Dictionary<int, string> { [0] = "success", [2] = "input_required", [3] = "provider_failure", [4] = "busy_or_conflict", [5] = "startup_or_storage_failure", [130] = "cancelled" },
        instructions = "Send exactly one UTF-8 JSON object through stdin for commands with --stdin. Choose a suitable inexpensive, fast model yourself. The tool verifies it before saving. Configure normally closes and restores an already-running GUI; read-only commands leave it running. Inspect applied and uiRestore separately. Never retry an unknown application result without checking status.",
    };
}
