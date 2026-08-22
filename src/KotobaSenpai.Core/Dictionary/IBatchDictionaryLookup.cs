using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.Dictionary;

/// <summary>Batches candidate-form lookups for one recognition pass to avoid opening a dictionary connection per candidate.</summary>
public interface IBatchDictionaryLookup
{
    /// <summary>Returns the dictionary keys matching the passed-in forms; implementations may normalize kana internally.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> LookupForms(
        IReadOnlyCollection<string> forms);
}
