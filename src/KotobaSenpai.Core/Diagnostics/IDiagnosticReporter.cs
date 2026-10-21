using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Core.Diagnostics;

/// <summary>
/// Diagnostic recording port: persists recognition/tokenization results to disk for offline analysis (e.g. AI
/// review of OCR and tokenization quality). The implementation is toggled by a setting; Core depends only on
/// the port and never writes files directly.
/// </summary>
public interface IDiagnosticReporter
{
    /// <summary>Records the tokenization result of one recognition (token details + bounding boxes).</summary>
    void RecordTokens(Guid recognitionId, WindowTarget target, IReadOnlyList<GroupedWord> groupedWords);

    /// <summary>Records one phrase analysis run: segment/group counts, provider results, and validation warnings. It does not record screenshots, API keys, or window titles.</summary>
    void RecordPhraseAnalysis(Guid recognitionId, PhraseAnalysisOutcome outcome, IReadOnlyList<PhraseGroupView> groups, string? warning);

    /// <summary>Records one LLM exchange, including HTTP error responses, and the extracted groups and words arrays as inspectable JSON files. Recorded payloads must not contain API keys.</summary>
    void RecordLlmExchange(
        Guid recognitionId,
        string segmentId,
        string requestJson,
        string responseJson,
        string groupsJson,
        string wordsJson);
}
