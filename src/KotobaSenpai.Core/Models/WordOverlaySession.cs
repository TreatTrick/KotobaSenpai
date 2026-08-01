using KotobaSenpai.Core.Models.Rules;
using KotobaSenpai.Core.SeedWork;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// 一次识别会话的聚合根，保证覆盖层只属于当前目标窗口，并按整体替换刷新词列表。
/// </summary>
public sealed class WordOverlaySession : Entity, IAggregateRoot
{
    private readonly List<ScreenWord> _words;

    private WordOverlaySession(Guid id, WindowTarget target, IReadOnlyList<ScreenWord> words)
    {
        Id = id;
        Target = target;
        _words = words.ToList();
    }

    public Guid Id { get; }

    public WindowTarget Target { get; }

    public IReadOnlyList<ScreenWord> Words => _words;

    /// <summary>每个词框底部内侧的一条下划线；刷新时整体替换，不留残留。</summary>
    public IReadOnlyList<OverlayLine> Lines => _words
        .Select(word => new OverlayLine(
            word.Bounds.X,
            Math.Max(word.Bounds.Y, word.Bounds.Bottom - 2),
            word.Bounds.Width))
        .ToArray();

    /// <summary>启动一个新会话：过滤零宽词，并校验目标窗口不变量。</summary>
    public static WordOverlaySession Start(WindowTarget target, IEnumerable<ScreenWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var validWords = words.Where(word => word.Bounds.Width > 0).ToArray();
        CheckRule(new OverlayTargetMustBeSpecifiedRule(target));
        return new WordOverlaySession(Guid.NewGuid(), target, validWords);
    }
}
