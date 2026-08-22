using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.Dictionary;

/// <summary>Port: looks up the dictionary by a tokenized token and returns the matching entries.</summary>
public interface IDictionaryLookup
{
    IReadOnlyList<DictionaryEntry> Lookup(Token token);
}
