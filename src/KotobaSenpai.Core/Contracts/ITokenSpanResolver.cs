using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>把 UniDic token 序列解析为非重叠、可查词的最终 span。</summary>
public interface ITokenSpanResolver
{
    IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens);

    /// <summary>
    /// 解析一次 OCR 中的多行 token。默认实现逐行回退；批量实现可共享一次词典查询。
    /// </summary>
    IReadOnlyList<IReadOnlyList<LookupSpan>> ResolveMany(
        IReadOnlyList<IReadOnlyList<Token>> tokenLines)
        => tokenLines.Select(Resolve).ToArray();
}
