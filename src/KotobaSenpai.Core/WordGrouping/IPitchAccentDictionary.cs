namespace KotobaSenpai.Core.WordGrouping;

public interface IPitchAccentDictionary
{
    IReadOnlyDictionary<(string Surface, string Reading), IReadOnlyList<int>> FindByKeys(
        IReadOnlyCollection<(string Surface, string Reading)> keys);
}
