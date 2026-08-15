namespace KotobaSenpai.Core.Models;

/// <summary>
/// 一个已验证的 phrase group。模型仅提供请求内 <see cref="ModelGroupId"/>；应用在验证后分配
/// <see cref="SessionGroupId"/>，并以其作为所有 part 的共享身份。提供方顺序经 <see cref="ProviderOrder"/>
/// 保留，用于悬停重叠时的决胜。
/// </summary>
public sealed record PhraseGroup
{
    public const int MaxGroupsPerSegment = 8;
    public const int MaxLabelLength = 64;
    public const int MaxMeaningLength = 256;
    public const int MaxGrammarLength = 512;

    public PhraseGroup(
        string modelGroupId,
        string type,
        IReadOnlyList<PhraseGroupPart> parts,
        string label,
        string meaning,
        string grammar,
        Guid? sessionGroupId = null,
        int providerOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(modelGroupId))
            throw new ArgumentException("Model group id must not be empty.", nameof(modelGroupId));
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
            throw new ArgumentException("A phrase group must contain at least one part.", nameof(parts));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A phrase group label must not be empty.", nameof(label));
        if (string.IsNullOrWhiteSpace(meaning))
            throw new ArgumentException("A phrase group meaning must not be empty.", nameof(meaning));
        if (string.IsNullOrWhiteSpace(grammar))
            throw new ArgumentException("A phrase group grammar explanation must not be empty.", nameof(grammar));
        if (providerOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(providerOrder));

        ModelGroupId = modelGroupId;
        Type = type;
        Parts = parts.ToArray();
        Label = label;
        Meaning = meaning;
        Grammar = grammar;
        SessionGroupId = sessionGroupId ?? Guid.Empty;
        ProviderOrder = providerOrder;
    }

    /// <summary>请求内模型返回的 group ID（仅请求内唯一，跨请求不唯一）。</summary>
    public string ModelGroupId { get; }

    public string Type { get; }

    /// <summary>一个或多个连续 part；part 之间可被其他 token 间隔。</summary>
    public IReadOnlyList<PhraseGroupPart> Parts { get; }

    public string Label { get; }

    public string Meaning { get; }

    public string Grammar { get; }

    /// <summary>应用分配的会话 group ID；验证后非空，作为所有 part/hover/弹窗的共享身份。</summary>
    public Guid SessionGroupId { get; }

    /// <summary>提供方响应中的出现顺序（0 起）。</summary>
    public int ProviderOrder { get; }

    /// <summary>本 group 引用的不同 token 个数，用于悬停重叠决胜（更少者优先）。</summary>
    public int DistinctTokenCount => Parts.SelectMany(part => part.Tokens).Select(token => token.Id).Distinct().Count();

    public PhraseGroup WithSessionId(Guid sessionGroupId) => new(
        ModelGroupId, Type, Parts, Label, Meaning, Grammar, sessionGroupId, ProviderOrder);

    public PhraseGroup WithProviderOrder(int providerOrder) => new(
        ModelGroupId, Type, Parts, Label, Meaning, Grammar, SessionGroupId, providerOrder);
}