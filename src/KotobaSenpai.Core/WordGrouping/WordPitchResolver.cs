using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.WordGrouping;

/// <summary>Resolves whole-span pitch in a batch and fills in corrected-token references for misses.</summary>
public sealed class WordPitchResolver(ISpanPitchResolver spanPitchResolver, IWordPitchLookup wordPitchLookup)
{
    internal void ApplyTo(GroupedWord[] words)
    {
        if (words.Length == 0)
            return;

        var pitches = spanPitchResolver.Resolve(words);
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            words[index] = pitches[index] is { } pitch
                ? word.WithSpanPitch(pitch)
                : word.WithDictionaryWordPitch(wordPitchLookup);
        }
    }
}
