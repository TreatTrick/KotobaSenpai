using System.Globalization;
using System.Resources;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Shared provider prompt resources; each CLI invocation uses its selected application culture.</summary>
public sealed class LlmPromptLocalizer(CultureInfo culture) : IStringLocalizer
{
    public static ResourceManager Resources { get; } = new(
        "KotobaSenpai.Platform.Windows.Llm.Resources.Prompts", typeof(LlmPromptLocalizer).Assembly);

    public string Get(string key, params object[] args)
    {
        var value = Resources.GetString(key, culture) ?? key;
        return args.Length == 0 ? value : string.Format(culture, value, args);
    }

    public event EventHandler? CultureChanged { add { } remove { } }
}
