using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Resolves a UniDic token sequence into non-overlapping, lookupable final spans.</summary>
public interface ITokenSpanResolver
{
    IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens);

    /// <summary>
    /// Resolves multiple lines of tokens from one OCR pass. The default implementation falls back to
    /// per-line resolution; batch implementations can share a single dictionary query.
    /// </summary>
    IReadOnlyList<IReadOnlyList<LookupSpan>> ResolveMany(
        IReadOnlyList<IReadOnlyList<Token>> tokenLines)
        => tokenLines.Select(Resolve).ToArray();
}
