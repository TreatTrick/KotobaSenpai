namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>settings.json keys and defaults for the BYOK LLM configuration (any provider supporting the configured protocol).</summary>
public static class LlmSettingsKeys
{
    public const string ApiKey = "LlmApiKey";
    public const string Endpoint = "LlmEndpoint";
    public const string Model = "LlmModel";
    public const string Protocol = "LlmProtocol";
    public const string Enabled = "PhraseGroupsEnabled";
}