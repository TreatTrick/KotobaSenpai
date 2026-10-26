using KotobaSenpai.Core.Settings;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotobaSenpai.Core.Llm;

/// <summary>One immutable configuration captured before a provider request.</summary>
public sealed record LlmConfiguration(string Protocol, string Endpoint, string ApiKey, string Model)
{
    public static IReadOnlyList<string> Keys { get; } =
        [LlmSettingsKeys.Protocol, LlmSettingsKeys.Endpoint, LlmSettingsKeys.ApiKey, LlmSettingsKeys.Model];

    public static LlmConfiguration Read(ISettingsService settings) => FromSnapshot(settings.GetSnapshot(Keys));

    public static LlmConfiguration FromSnapshot(IReadOnlyDictionary<string, string?> values) => new(
        LlmProtocolIds.Normalize(values[LlmSettingsKeys.Protocol]),
        values[LlmSettingsKeys.Endpoint] ?? "", values[LlmSettingsKeys.ApiKey] ?? "", values[LlmSettingsKeys.Model] ?? "");

    public IReadOnlyDictionary<string, string?> ToSnapshot() => new Dictionary<string, string?>
    {
        [LlmSettingsKeys.Protocol] = Protocol,
        [LlmSettingsKeys.Endpoint] = Endpoint,
        [LlmSettingsKeys.ApiKey] = ApiKey,
        [LlmSettingsKeys.Model] = Model,
    };

    internal string Fingerprint() => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { Protocol, Endpoint, ApiKey, Model })));

    public override string ToString() => $"{Protocol}: model configured={Model.Length > 0}, key configured={ApiKey.Length > 0}";
}
