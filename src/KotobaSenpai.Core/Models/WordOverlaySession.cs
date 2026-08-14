using KotobaSenpai.Core.Models.Rules;
using KotobaSenpai.Core.SeedWork;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// 一次识别会话的聚合根，保证覆盖层只属于当前目标窗口，并按整体替换刷新词列表。
/// </summary>
public sealed class WordOverlaySession : Entity, IAggregateRoot
{
    private readonly List<GroupedWord> _words;
    private readonly List<PhraseGroupView> _phraseGroups;

    private WordOverlaySession(
        Guid id,
        WindowTarget target,
        IReadOnlyList<GroupedWord> words,
        IReadOnlyList<PhraseGroupView> phraseGroups,
        string? phraseWarning)
    {
        Id = id;
        Target = target;
        _words = words.ToList();
        _phraseGroups = phraseGroups.ToList();
        PhraseWarning = phraseWarning;
    }

    public Guid Id { get; }

    public WindowTarget Target { get; }

    public IReadOnlyList<GroupedWord> Words => _words;

    /// <summary>已验证的 phrase group（含逐 part 屏幕几何）；无 phrase 分析时为空。</summary>
    public IReadOnlyList<PhraseGroupView> PhraseGroups => _phraseGroups;

    /// <summary>phrase 分析的可重试警告；无失败时为 null。</summary>
    public string? PhraseWarning { get; }

    /// <summary>每个词框底部内侧的一条下划线；刷新时整体替换，不留残留。</summary>
    public IReadOnlyList<OverlayLine> Lines => _words
        .Select(word => new OverlayLine(
            word.Bounds.X,
            Math.Max(word.Bounds.Y, word.Bounds.Bottom - 2),
            word.Bounds.Width))
        .ToArray();

    /// <summary>启动一个新会话：过滤零宽词，并校验目标窗口不变量。</summary>
    public static WordOverlaySession Start(
        WindowTarget target,
        IEnumerable<GroupedWord> words,
        IEnumerable<PhraseGroupView>? phraseGroups = null,
        string? phraseWarning = null)
    {
        ArgumentNullException.ThrowIfNull(words);

        var validWords = words.Where(word => word.Bounds.Width > 0).ToArray();
        CheckRule(new OverlayTargetMustBeSpecifiedRule(target));
        return new WordOverlaySession(
            Guid.NewGuid(),
            target,
            validWords,
            (phraseGroups ?? Array.Empty<PhraseGroupView>()).ToArray(),
            phraseWarning);
    }
}
