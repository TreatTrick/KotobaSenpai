namespace KotobaSenpai.Core.Models;

/// <summary>
/// 一个受 UniDic token 边界约束的可查词 span。
/// <para>
/// <see cref="Tokens"/> 保留原始形态素；<see cref="Token"/> 是供现有 UI/诊断接口使用的
/// 合并词面视图。词典结果在识别阶段附着到 span，避免悬停时再从单个字符重新猜词。
/// </para>
/// </summary>
public sealed record LookupSpan
{
    public LookupSpan(
        IReadOnlyList<Token> tokens,
        string lookupKey,
        IReadOnlyList<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(lookupKey);
        ArgumentNullException.ThrowIfNull(entries);
        if (tokens.Count == 0)
            throw new ArgumentException("A lookup span must contain at least one token.", nameof(tokens));

        Tokens = tokens.ToArray();
        LookupKey = lookupKey;
        Entries = entries.ToArray();
        Surface = string.Concat(Tokens.Select(token => token.Surface));
        Reading = string.Concat(Tokens.Select(token => token.Reading));
        StartOffset = Tokens[0].StartOffset;
        EndOffset = Tokens[^1].StartOffset + Tokens[^1].Surface.Length;
        Token = CreateDisplayToken(Tokens, lookupKey, Surface, Reading);
    }

    /// <summary>组成该 span 的原始 UniDic token，按输入顺序排列。</summary>
    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>供兼容现有 popup/诊断 API 的合并 token 视图。</summary>
    public Token Token { get; }

    /// <summary>实际覆盖的 OCR 文字。</summary>
    public string Surface { get; }

    /// <summary>组成 token 的出现读音拼接。</summary>
    public string Reading { get; }

    /// <summary>用于词典命中的键（直接词面或基础 token lemma）。</summary>
    public string LookupKey { get; }

    /// <summary>输入字符串中的 UTF-16 起止偏移，范围为 [StartOffset, EndOffset)。</summary>
    public int StartOffset { get; }

    public int EndOffset { get; }

    /// <summary>本 span 在识别阶段得到的词典结果；未命中时为空。</summary>
    public IReadOnlyList<DictionaryEntry> Entries { get; }

    private static Token CreateDisplayToken(
        IReadOnlyList<Token> tokens,
        string lookupKey,
        string surface,
        string reading)
    {
        var first = tokens[0];
        return new Token(
            surface,
            lookupKey,
            lookupKey,
            reading,
            string.Concat(tokens.Select(token => token.BaseReading)),
            string.Concat(tokens.Select(token => token.Pronunciation)),
            first.PartsOfSpeech,
            first.ConjugationType,
            first.ConjugationForm,
            first.AType,
            first.StartOffset);
    }
}
