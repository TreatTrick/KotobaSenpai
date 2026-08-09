using System.Globalization;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 逐行对 OCR 字符做分词，把 token 的 span 映射回成员字符框并求并集，生成每词一条下划线几何。
/// 范围：全部词含助词，仅排除标点与空白类 token；映射不到字符框的 token 跳过。
/// </summary>
public sealed class WordGroupingService : IOcrWordGroupingService
{
    private readonly ITokenizer _tokenizer;

    public WordGroupingService(ITokenizer tokenizer)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    }

    public IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new List<GroupedWord>();
        foreach (var line in lines)
        {
            if (line.Words.Count == 0)
                continue;

            // 每行独立分词，token 只在行内映射 → 每条下划线始终是单个并集矩形（词不跨视觉行）。
            var text = line.Text;
            foreach (var token in _tokenizer.Tokenize(text))
            {
                if (IsSeparatorToken(token.Surface))
                    continue;

                var start = Math.Max(0, token.StartOffset);
                var end = Math.Min(line.Words.Count, start + token.Surface.Length);
                if (start >= end)
                    continue;

                result.Add(new GroupedWord(token, Union(line.Words, start, end)));
            }
        }
        return result;
    }

    /// <summary>对 [start, end) 的成员字符框求并集包围盒（每词一个宽度合法矩形）。</summary>
    private static ScreenRect Union(IReadOnlyList<OcrWord> words, int start, int end)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
        for (int i = start; i < end; i++)
        {
            var b = words[i].FrameBounds;
            x1 = Math.Min(x1, b.X);
            y1 = Math.Min(y1, b.Y);
            x2 = Math.Max(x2, b.Right);
            y2 = Math.Max(y2, b.Bottom);
        }
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static bool IsSeparatorToken(string surface)
        => surface.Length > 0 && surface.All(c => char.IsWhiteSpace(c) || IsPunctuation(c));

    private static bool IsPunctuation(char c)
        => char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };
}