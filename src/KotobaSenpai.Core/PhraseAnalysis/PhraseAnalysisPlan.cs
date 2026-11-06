using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>
/// One prepared OCR layout shared by local grouping, cached preview, and provider analysis.
/// A source line can be split at a completed boundary while retaining its original character boxes.
/// </summary>
public sealed class PhraseAnalysisPlan
{
    internal PhraseAnalysisPlan(Guid recognitionId, int cacheGeneration, IReadOnlyList<OcrLine> lines,
        IReadOnlyList<SentenceSegment> segments, IReadOnlyList<PhraseAnalysisRequest> requests)
    {
        RecognitionId = recognitionId;
        CacheGeneration = cacheGeneration;
        Lines = lines.ToArray();
        Segments = segments.ToArray();
        Requests = requests.ToArray();
    }

    public IReadOnlyList<OcrLine> Lines { get; }
    public IReadOnlyList<SentenceSegment> Segments { get; }
    internal Guid RecognitionId { get; }
    internal int CacheGeneration { get; }
    internal IReadOnlyList<PhraseAnalysisRequest> Requests { get; }
}
