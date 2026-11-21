using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.WordGrouping;

public interface ISpanPitchResolver
{
    /// <summary>Resolves one optional pitch result per word in input order.</summary>
    IReadOnlyList<SpanPitch?> Resolve(IReadOnlyList<GroupedWord> words);
}
