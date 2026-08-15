namespace KotobaSenpai.Core.Localization;

/// <summary>
/// Localization port: resolves a localized string for the current culture by resource key (plus optional format
/// arguments), and notifies subscribers via <see cref="CultureChanged"/> to refresh immediately when the culture
/// is switched at runtime.
/// <para>
/// The port lives in Core (zero external dependencies, BCL-only); the concrete implementation lives in App and
/// the ViewModel depends only on this interface, so it can be verified with a fake in desktop-less tests.
/// </para>
/// </summary>
public interface IStringLocalizer
{
    /// <summary>Resolves a localized string by key; {0} placeholders in the resource value are replaced by <paramref name="args"/>.</summary>
    string Get(string key, params object[] args);

    /// <summary>Raised when the current UI culture is switched at runtime; subscribers should recompute their displayed localized properties.</summary>
    event EventHandler? CultureChanged;
}
