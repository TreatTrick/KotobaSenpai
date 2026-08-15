using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: looks up the dictionary by a tokenized token and returns the matching entries.</summary>
public interface IDictionaryLookup
{
    IReadOnlyList<DictionaryEntry> Lookup(Token token);
}
