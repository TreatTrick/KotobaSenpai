using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.Dictionary;

/// <summary>Resolves a UniDic token sequence into non-overlapping, lookupable final spans.</summary>
public interface ITokenSpanResolver
{
    IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens);
}
