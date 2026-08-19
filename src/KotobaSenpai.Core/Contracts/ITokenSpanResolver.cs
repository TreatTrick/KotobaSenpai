using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Resolves a UniDic token sequence into non-overlapping, lookupable final spans.</summary>
public interface ITokenSpanResolver
{
    IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens);
}
