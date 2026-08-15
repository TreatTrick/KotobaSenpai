namespace KotobaSenpai.Core.Models;

/// <summary>
/// A sentence-level token reference binding a token in the OCR reading order to its source line and in-line
/// position, carrying UniDic metadata and the source character boxes.
/// <para>
/// <see cref="Id"/> is a request-scoped stable id (e.g. <c>l0:t3</c>) for the LLM to reference;
/// <see cref="SentenceIndex"/> is the token's global ordinal within the whole sentence's reading order, used to
/// tell whether tokens within a part are contiguous. In-line offsets and character boxes stay local for geometry mapping.
/// </para>
/// </summary>
public sealed record SentenceTokenReference
{
    public SentenceTokenReference(
        int sentenceIndex,
        int lineId,
        int lineTokenIndex,
        int lineOffset,
        Token token,
        IReadOnlyList<ScreenRect> boxes)
    {
        if (sentenceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sentenceIndex), "Sentence index must be non-negative.");
        if (lineId < 0)
            throw new ArgumentOutOfRangeException(nameof(lineId), "Line id must be non-negative.");
        if (lineTokenIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(lineTokenIndex), "Line token index must be non-negative.");
        if (lineOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(lineOffset), "Line offset must be non-negative.");
        ArgumentNullException.ThrowIfNull(token);

        SentenceIndex = sentenceIndex;
        LineId = lineId;
        LineTokenIndex = lineTokenIndex;
        LineOffset = lineOffset;
        Token = token;
        Boxes = (boxes ?? throw new ArgumentNullException(nameof(boxes))).ToArray();
    }

    /// <summary>The token's global ordinal within the whole sentence's reading order.</summary>
    public int SentenceIndex { get; }

    /// <summary>The source OCR line index.</summary>
    public int LineId { get; }

    /// <summary>The token's ordinal within its source line.</summary>
    public int LineTokenIndex { get; }

    /// <summary>The token's UTF-16 start offset within the source line text.</summary>
    public int LineOffset { get; }

    /// <summary>UniDic tokenization metadata.</summary>
    public Token Token { get; }

    /// <summary>The source character boxes covered by this token (in-line physical coordinates).</summary>
    public IReadOnlyList<ScreenRect> Boxes { get; }

    /// <summary>Request-scoped stable reference id (a strongly typed value object, wire format <c>l0:t3</c>).</summary>
    public SentenceTokenId Id => new(LineId, LineTokenIndex);
}