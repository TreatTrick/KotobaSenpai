using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>
/// One prepared OCR layout shared by local grouping, cached preview, and provider analysis.
/// A source line can be split at a completed boundary while retaining its original character boxes.
/// </summary>
public sealed class PhraseAnalysisPlan
{
    internal PhraseAnalysisPlan(Guid recognitionId, long epoch, IReadOnlyList<OcrLine> lines,
        IReadOnlyList<SentenceSegment> segments, IReadOnlyList<PhraseAnalysisRequest> requests)
    {
        RecognitionId = recognitionId;
        Epoch = epoch;
        Lines = lines.ToArray();
        Segments = segments.ToArray();
        Requests = requests.ToArray();
    }

    public IReadOnlyList<OcrLine> Lines { get; }
    public IReadOnlyList<SentenceSegment> Segments { get; }
    internal Guid RecognitionId { get; }
    internal long Epoch { get; }
    internal IReadOnlyList<PhraseAnalysisRequest> Requests { get; }
}
