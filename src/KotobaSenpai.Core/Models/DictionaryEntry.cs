namespace KotobaSenpai.Core.Models;

/// <summary>词典条目：头词（表记）、读音与各义项。</summary>
public sealed record DictionaryEntry(string Headword, string Reading, IReadOnlyList<DictionarySense> Senses);