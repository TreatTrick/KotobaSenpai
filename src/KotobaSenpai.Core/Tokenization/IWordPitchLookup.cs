namespace KotobaSenpai.Core.Tokenization;

/// <summary>Looks up lexical pitch variants for an exact surface and corrected reading.</summary>
public interface IWordPitchLookup
{
    IReadOnlyList<int> FindWordPitch(Token source, string reading);
}

/// <summary>UniDic pitch variants for a single word, independent of its phrase's pitch.</summary>
public sealed record WordPitchReference(string Surface, string Reading, IReadOnlyList<int> Positions);
