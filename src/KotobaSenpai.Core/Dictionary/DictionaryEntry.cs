namespace KotobaSenpai.Core.Dictionary;

/// <summary>A dictionary entry: headword (orthography), reading, and each sense.</summary>
public sealed record DictionaryEntry(string Headword, string Reading, IReadOnlyList<DictionarySense> Senses);