namespace KotobaSenpai.Core.Models;

/// <summary>
/// 句级 token 引用：把 OCR 阅读顺序中的 token 绑定到源行与行内位置，并携带 UniDic 元数据和源字符框。
/// <para>
/// <see cref="Id"/> 是请求内稳定 ID（如 <c>l0:t3</c>），供 LLM 引用；<see cref="SentenceIndex"/> 是该 token
/// 在整句阅读顺序中的全局序号，用于判断 part 内 token 是否连续。行内偏移与字符框保持本地，供几何映射。
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

    /// <summary>整句阅读顺序中的全局 token 序号。</summary>
    public int SentenceIndex { get; }

    /// <summary>源 OCR 行索引。</summary>
    public int LineId { get; }

    /// <summary>该 token 在其源行内的 token 序号。</summary>
    public int LineTokenIndex { get; }

    /// <summary>该 token 在源行文本内的 UTF-16 起始偏移。</summary>
    public int LineOffset { get; }

    /// <summary>UniDic 分词元数据。</summary>
    public Token Token { get; }

    /// <summary>该 token 覆盖的源字符框（行内物理坐标）。</summary>
    public IReadOnlyList<ScreenRect> Boxes { get; }

    /// <summary>请求内稳定引用 ID（强类型值对象，线格式 <c>l0:t3</c>）。</summary>
    public SentenceTokenId Id => new(LineId, LineTokenIndex);
}