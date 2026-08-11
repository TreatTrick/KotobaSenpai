namespace KotobaSenpai.Core.Models;

/// <summary>OCR 字符映射到最终查词 span 后的屏幕词块。</summary>
public sealed record GroupedWord(Token Token, ScreenRect Bounds)
{
    public GroupedWord(LookupSpan span, ScreenRect bounds)
        : this(
            span?.Token ?? throw new ArgumentNullException(nameof(span)),
            bounds,
            span.Tokens,
            span.LookupKey,
            span.Entries,
            hasResolvedLookup: true)
    {
    }

    private GroupedWord(
        Token token,
        ScreenRect bounds,
        IReadOnlyList<Token> sourceTokens,
        string lookupKey,
        IReadOnlyList<DictionaryEntry> entries,
        bool hasResolvedLookup)
        : this(token, bounds)
    {
        SourceTokens = sourceTokens;
        LookupKey = lookupKey;
        Entries = entries;
        HasResolvedLookup = hasResolvedLookup;
    }

    /// <summary>组成合并词块的原始 UniDic token；旧调用方构造时默认为当前 token。</summary>
    public IReadOnlyList<Token> SourceTokens { get; } = [Token];

    public string Surface => Token.Surface;

    public string Reading => Token.Reading;

    public string LookupKey { get; } = Token.Lemma;

    public IReadOnlyList<DictionaryEntry> Entries { get; } = Array.Empty<DictionaryEntry>();

    /// <summary>区分“已预解析但未命中”和旧调用方尚未执行预解析。</summary>
    public bool HasResolvedLookup { get; }

    /// <summary>只替换坐标，复用已解析的 token/span/entries 引用。</summary>
    public GroupedWord WithBounds(ScreenRect bounds)
        => new(Token, bounds, SourceTokens, LookupKey, Entries, HasResolvedLookup);

    /// <summary>新增的预解析元数据不改变原 positional record 的值相等语义。</summary>
    public bool Equals(GroupedWord? other)
        => ReferenceEquals(this, other)
            || (other is not null && Token == other.Token && Bounds == other.Bounds);

    public override int GetHashCode() => HashCode.Combine(Token, Bounds);
}
