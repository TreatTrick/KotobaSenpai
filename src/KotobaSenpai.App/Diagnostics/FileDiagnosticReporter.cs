using System.IO;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Diagnostics;

/// <summary>
/// 诊断记录：设置项 <c>DiagEnabled</c> 为 "true" 时，把分词结果（token 细节 + 包围盒）
/// 写到 <c>%LocalAppData%/KotobaSenpai/diag/</c>，与识别器保存的截图/OCR 结构同目录，供离线分析。
/// </summary>
public sealed class FileDiagnosticReporter : IDiagnosticReporter
{
    private const string DiagEnabledKey = "DiagEnabled";

    private readonly ISettingsService _settings;

    public FileDiagnosticReporter(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void RecordTokens(WindowTarget target, IReadOnlyList<GroupedWord> groupedWords)
    {
        if (!string.Equals(_settings.GetValue(DiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase))
            return;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "diag");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            $"target={target.Title} bounds={target.Bounds}",
            $"tokens={groupedWords.Count}",
            "",
            "## 分词器结果",
        };
        for (int i = 0; i < groupedWords.Count; i++)
        {
            var word = groupedWords[i];
            var token = word.Token;
            lines.Add($"{i + 1}. surface={token.Surface} | lemma={token.Lemma} | reading={token.Reading} | pos={token.PartsOfSpeech.Pos1} | start={token.StartOffset} | bounds={word.Bounds}");
        }
        File.WriteAllLines(Path.Combine(dir, $"tokens-{DateTime.Now:HHmmss-fff}.txt"), lines);
    }
}