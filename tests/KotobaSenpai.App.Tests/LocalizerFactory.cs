using System.Globalization;
using System.Resources;
using KotobaSenpai.App.Localization;

namespace KotobaSenpai.App.Tests;

/// <summary>Test helper: builds a localizer from the real embedded Strings resources, with a controllable initial culture.</summary>
internal static class LocalizerFactory
{
    public static ResourceManagerStringLocalizer Create(CultureInfo initialCulture)
        => new(new ResourceManager(
                   "KotobaSenpai.App.Resources.Strings",
                   typeof(ResourceManagerStringLocalizer).Assembly),
               initialCulture);
}
