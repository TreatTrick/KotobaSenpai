namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>Persisted explanation language, independent of the application's UI language.</summary>
public interface IExplanationLanguagePreferences
{
    string? CustomLanguage { get; }
    string EffectiveLanguage { get; }
    event EventHandler? EffectiveLanguageChanged;

    /// <summary>Saves a trimmed language name, or follows the UI when blank; rejects invalid input without changes.</summary>
    bool TrySave(string? language);
}
