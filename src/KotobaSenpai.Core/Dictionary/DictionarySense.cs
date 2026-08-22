namespace KotobaSenpai.Core.Dictionary;

/// <summary>One dictionary sense: part-of-speech tags and English glosses.</summary>
public sealed record DictionarySense(IReadOnlyList<string> Pos, IReadOnlyList<string> Glosses);