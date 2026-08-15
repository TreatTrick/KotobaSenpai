using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>
/// Diagnostic recording port: persists recognition/tokenization results to disk for offline analysis (e.g. AI
/// review of OCR and tokenization quality). The implementation is toggled by a setting; Core depends only on
/// the port and never writes files directly.
/// </summary>
public interface IDiagnosticReporter
{
    /// <summary>Records the tokenization result of one recognition (token details + bounding boxes).</summary>
    void RecordTokens(WindowTarget target, IReadOnlyList<GroupedWord> groupedWords);

    /// <summary>Records one phrase analysis run: segment/group counts, provider results, and validation warnings. It does not record screenshots, API keys, or window titles.</summary>
    void RecordPhraseAnalysis(PhraseAnalysisOutcome outcome, IReadOnlyList<PhraseGroupView> groups, string? warning);
}