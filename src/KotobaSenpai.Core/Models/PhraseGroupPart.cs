namespace KotobaSenpai.Core.Models;

/// <summary>
/// 一个 phrase group part：有序、连续、非空的 token 引用序列。part 内部不得有间隔，
/// 词面与读音由所引 token 本地推导，不信任模型文本。
/// </summary>
public sealed record PhraseGroupPart
{
    public PhraseGroupPart(IReadOnlyList<SentenceTokenReference> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
            throw new ArgumentException("A phrase part must reference at least one token.", nameof(tokens));

        Tokens = tokens.ToArray();
        Surface = string.Concat(Tokens.Select(reference => reference.Token.Surface));
        Reading = string.Concat(Tokens.Select(reference => reference.Token.Reading));
    }

    /// <summary>按阅读顺序排列、连续无间隔的 token 引用。</summary>
    public IReadOnlyList<SentenceTokenReference> Tokens { get; }

    /// <summary>由所引 token 词面本地拼接的显示文本。</summary>
    public string Surface { get; }

    /// <summary>由所引 token 读音本地拼接的显示读音。</summary>
    public string Reading { get; }
}