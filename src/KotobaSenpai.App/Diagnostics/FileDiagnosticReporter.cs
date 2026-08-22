using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Diagnostics;

/// <summary>
/// Diagnostics: when the setting <c>DiagEnabled</c> is "true", writes the final lookup phrase groups (source token details + bounding boxes)
/// and LLM exchange artifacts to <c>%LocalAppData%/KotobaSenpai/diag/</c>, in the same directory as the screenshots/OCR that the recognizer saves, for offline analysis.
/// </summary>
public sealed class FileDiagnosticReporter : IDiagnosticReporter
{
    private const string DiagEnabledKey = "DiagEnabled";

    private readonly ISettingsService _settings;
    private readonly string _diagnosticDirectory;
    private static int _seq;

    public FileDiagnosticReporter(ISettingsService settings, string? diagnosticDirectory = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _diagnosticDirectory = diagnosticDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "diag");
    }

    public void RecordTokens(Guid recognitionId, WindowTarget target, IReadOnlyList<GroupedWord> groupedWords)
    {
        if (!string.Equals(_settings.GetValue(DiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase))
            return;

        var dir = _diagnosticDirectory;
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            $"target={target.Title} bounds={target.Bounds}",
            $"tokens={groupedWords.Count} spans={groupedWords.Count}",
            "",
            "## word lookup results",
        };
        for (int i = 0; i < groupedWords.Count; i++)
        {
            var word = groupedWords[i];
            var token = word.Token;
            var source = string.Join("+", word.SourceTokens.Select(sourceToken => sourceToken.Surface));
            lines.Add($"{i + 1}. source={source} | surface={token.Surface} | lookup={word.LookupKey} | reading={token.Reading} | pos={token.PartsOfSpeech.Pos1} | start={token.StartOffset} | entries={word.Entries.Count} | bounds={word.Bounds}");
        }
        File.WriteAllLines(Path.Combine(dir, $"tokens-{recognitionId:N}.txt"), lines, Utf8Bom);
        PruneToLatest(dir, "tokens-");
    }

    public void RecordPhraseAnalysis(Guid recognitionId, PhraseAnalysisOutcome outcome, IReadOnlyList<PhraseGroupView> groups, string? warning)
    {
        if (!string.Equals(_settings.GetValue(DiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase))
            return;

        var dir = _diagnosticDirectory;
        Directory.CreateDirectory(dir);

        var flags = new[] { "## phrase analysis", $"outcome={outcome}", $"groups={groups.Count}" };
        if (!string.IsNullOrEmpty(warning))
            flags = flags.Append($"warning={warning}").ToArray();
        File.WriteAllLines(Path.Combine(dir, $"phrase-{recognitionId:N}.txt"), flags, Utf8Bom);
        PruneToLatest(dir, "phrase-");
    }

    public void RecordLlmExchange(
        Guid recognitionId,
        string segmentId,
        string requestJson,
        string responseJson,
        string groupsJson,
        string wordsJson)
    {
        if (!string.Equals(_settings.GetValue(DiagEnabledKey), "true", StringComparison.OrdinalIgnoreCase))
            return;

        var dir = _diagnosticDirectory;
        Directory.CreateDirectory(dir);

        var seq = Interlocked.Increment(ref _seq);
        var safe = Sanitize(segmentId);
        var stamp = $"{recognitionId:N}-{safe}-{seq:D3}";
        // Indented + UTF-8 with BOM so each file opens correctly in a Chinese-locale editor/JSON viewer; the request body never contains the API key (it lives in the Authorization header).
        File.WriteAllText(Path.Combine(dir, $"llm-req-{stamp}.json"), FormatJson(requestJson), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, $"llm-resp-{stamp}.json"), FormatJson(responseJson), Utf8Bom);
        PruneToLatest(dir, "llm-req-");
        PruneToLatest(dir, "llm-resp-");

        File.WriteAllText(Path.Combine(dir, $"llm-groups-{stamp}.json"), FormatJson(groupsJson), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, $"llm-words-{stamp}.json"), FormatJson(wordsJson), Utf8Bom);
        PruneToLatest(dir, "llm-groups-");
        PruneToLatest(dir, "llm-words-");
    }

    /// <summary>Keeps only the latest <paramref name="max"/> files whose name starts with <paramref name="prefix"/>, deleting older ones so diag never accumulates unboundedly.</summary>
    private static void PruneToLatest(string dir, string prefix, int max = 10)
    {
        foreach (var file in Directory.GetFiles(dir, $"{prefix}*")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(max))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static readonly Encoding Utf8Bom = new UTF8Encoding(true);
    // UnsafeRelaxedJsonEscaping writes CJK as literal UTF-8 (readable) instead of \uXXXX escapes; the "unsafe" caveat only concerns HTML contexts, irrelevant for a local diag file.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string FormatJson(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.ToJsonString(Indented) ?? json;
        }
        catch (JsonException)
        {
            return json; // not valid JSON; keep the raw bytes so nothing is lost
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
