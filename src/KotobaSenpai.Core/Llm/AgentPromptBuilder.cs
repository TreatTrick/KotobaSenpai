using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Llm;

/// <summary>Generates the current localized bootstrap prompt without reading stored credentials.</summary>
public static class AgentPromptBuilder
{
    public static string Build(IStringLocalizer localizer, string cliPath)
        => localizer.Get("Agent_Prompt", cliPath.Replace("'", "''", StringComparison.Ordinal),
            AgentApiContract.SchemaVersion);
}
