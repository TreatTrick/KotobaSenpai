namespace KotobaSenpai.Core.Models;

/// <summary>
/// 提供方返回、尚未校验的 group（线级模型）。part 保存强类型 token 引用 ID 而非引用，
/// 由编排器用请求内的 ID→引用映射解析并校验成 <see cref="PhraseGroup"/>；无效 group 被单独丢弃。
/// </summary>
public sealed record ParsedPhraseGroup
{
    public ParsedPhraseGroup(
        string modelGroupId,
        string type,
        IReadOnlyList<IReadOnlyList<SentenceTokenId>> partTokenIds,
        string label,
        string meaningZh,
        string grammarZh)
    {
        ArgumentNullException.ThrowIfNull(modelGroupId);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(partTokenIds);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(meaningZh);
        ArgumentNullException.ThrowIfNull(grammarZh);

        ModelGroupId = modelGroupId;
        Type = type;
        PartTokenIds = partTokenIds.Select(ids => (IReadOnlyList<SentenceTokenId>)ids.ToArray()).ToArray();
        Label = label;
        MeaningZh = meaningZh;
        GrammarZh = grammarZh;
    }

    /// <summary>请求内模型返回的 group ID。</summary>
    public string ModelGroupId { get; }

    public string Type { get; }

    /// <summary>每个 part 是一列强类型 token ID；part 之间可间隔，part 内须连续。</summary>
    public IReadOnlyList<IReadOnlyList<SentenceTokenId>> PartTokenIds { get; }

    public string Label { get; }

    public string MeaningZh { get; }

    public string GrammarZh { get; }
}