using System.Globalization;
using KotobaSenpai.App.Localization;
using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.App.Tests;

public sealed class AgentPromptTests
{
    [Theory]
    [InlineData("en", "repeat the four-line form above", "Optional fields may be left blank.", "do not ask me to fill in the tool path or interface version")]
    [InlineData("zh-CN", "逐行展示上面的四行填写模板", "标注“可选”的项目可以留空", "不要让我填写工具路径或接口版本")]
    public void Missing_details_use_a_four_line_form_with_explicit_placeholder_and_optional_field_instructions(
        string language, string formInstruction, string optionalInstruction, string bootstrapInstruction)
    {
        var localizer = new ResourceManagerStringLocalizer();
        localizer.ApplyCulture(new CultureInfo(language));
        var prompt = AgentPromptBuilder.Build(localizer, @"C:\App\KotobaSenpai.Cli.exe").Replace("\r\n", "\n");
        var lines = prompt.Split('\n');
        var keyLine = Array.FindIndex(lines, line => line.StartsWith(language == "en" ? "API key:" : "API Key：", StringComparison.Ordinal));
        Assert.True(keyLine >= 2, "The API key must follow the provider and optional documentation fields.");
        Assert.Contains(language == "en" ? "Provider URL:" : "服务商url：", lines[keyLine - 2]);
        Assert.Contains(language == "en" ? "(optional)" : "（可选）", lines[keyLine - 1]);
        Assert.Contains(language == "en" ? "(optional)" : "（可选）", lines[keyLine + 1]);
        Assert.Contains("YOUR_API_KEY", lines[keyLine]);
        Assert.Contains(formInstruction, prompt);
        Assert.Contains(optionalInstruction, prompt);
        Assert.Contains(bootstrapInstruction, prompt);
        Assert.Contains(language == "en" ? "Please fill in the <...> placeholders" : "请填写〈〉内的内容", prompt);
    }

    [Theory]
    [InlineData("en", "First run")]
    [InlineData("zh-CN", "首先")]
    [InlineData("fr", "First run")]
    public void Prompt_uses_active_language_and_an_executable_literal_path(string language, string expected)
    {
        var localizer = new ResourceManagerStringLocalizer();
        localizer.ApplyCulture(new CultureInfo(language));
        const string path = @"C:\Apps\O'Brien $tools\KotobaSenpai.Cli.exe";
        var prompt = AgentPromptBuilder.Build(localizer, path);
        Assert.Contains(expected, prompt);
        Assert.Contains(@"& 'C:\Apps\O''Brien $tools\KotobaSenpai.Cli.exe' capabilities --json", prompt);
        Assert.Contains("YOUR_API_KEY", prompt);
        Assert.DoesNotContain("{0}", prompt);
        Assert.DoesNotContain("{1}", prompt);
        Assert.DoesNotContain("{2}", prompt);
    }

    [Fact]
    public void Switching_language_rebuilds_the_complete_template()
    {
        var localizer = new ResourceManagerStringLocalizer();
        localizer.ApplyCulture(new CultureInfo("en"));
        var english = AgentPromptBuilder.Build(localizer, @"C:\App\KotobaSenpai.Cli.exe");
        localizer.ApplyCulture(new CultureInfo("zh-CN"));
        var chinese = AgentPromptBuilder.Build(localizer, @"C:\App\KotobaSenpai.Cli.exe");
        Assert.NotEqual(english, chinese);
        Assert.Contains("First run", english);
        Assert.Contains("首先", chinese);
        Assert.DoesNotContain("Reply language:", english);
        Assert.DoesNotContain("回复语言：", chinese);
        localizer.ApplyCulture(new CultureInfo("en"));
        Assert.Equal(english, AgentPromptBuilder.Build(localizer, @"C:\App\KotobaSenpai.Cli.exe"));
    }
}
