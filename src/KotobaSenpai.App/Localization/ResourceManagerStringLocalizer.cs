using System.Globalization;
using System.Resources;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// BCL implementation of <see cref="IStringLocalizer"/>: resolves embedded .resx through <see cref="ResourceManager"/>,
/// with a built-in culture fallback chain (active -> neutral English). Maintains the current culture locally and raises
/// <see cref="CultureChanged"/> on culture switch; does not modify the global <c>CurrentUICulture</c> directly (that is <see cref="LanguageService"/>'s job).
/// </summary>
public sealed class ResourceManagerStringLocalizer : IStringLocalizer
{
    /// <summary>The base name of the embedded resource (root namespace + Resources folder + Strings).</summary>
    private const string ResourceBaseName = "KotobaSenpai.App.Resources.Strings";

    private readonly ResourceManager _manager;
    private CultureInfo _culture;

    /// <summary>Production constructor: loads the <c>Strings</c> resource from this assembly, with the initial culture taken from the current UI culture.</summary>
    public ResourceManagerStringLocalizer()
        : this(new ResourceManager(ResourceBaseName, typeof(ResourceManagerStringLocalizer).Assembly),
               CultureInfo.CurrentUICulture)
    {
    }

    /// <summary>Test constructor: injects a custom <see cref="ResourceManager"/> and an initial culture.</summary>
    public ResourceManagerStringLocalizer(ResourceManager manager, CultureInfo initialCulture)
    {
        _manager = manager;
        _culture = initialCulture;
    }

    public event EventHandler? CultureChanged;

    /// <summary>The locally maintained current UI culture (kept in sync with the global CurrentUICulture by LanguageService).</summary>
    public CultureInfo CurrentCulture => _culture;

    /// <inheritdoc />
    public string Get(string key, params object[] args)
    {
        var value = _manager.GetString(key, _culture) ?? key;
        return args.Length == 0 ? value : string.Format(_culture, value, args);
    }

    /// <summary>Switches the local culture; when it actually changes, raises <see cref="CultureChanged"/> so subscribers refresh immediately.</summary>
    public void ApplyCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (culture.Name == _culture.Name)
            return;

        _culture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
