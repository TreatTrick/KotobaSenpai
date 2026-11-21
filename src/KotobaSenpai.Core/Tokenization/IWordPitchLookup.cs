namespace KotobaSenpai.Core.Tokenization;

/// <summary>Looks up lexical pitch variants for an exact surface and corrected reading.</summary>
public interface IWordPitchLookup
{
    IReadOnlyList<int> FindWordPitch(Token source, string reading);
}

/// <summary>UniDic word-level pitch variants displayed as a reference in the popup.</summary>
public sealed record WordPitchReference(string Surface, string Reading, IReadOnlyList<int> Positions);
