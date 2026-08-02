using System.Globalization;
using System.Resources;
using KotobaSenpai.App.Localization;

namespace KotobaSenpai.App.Tests;

/// <summary>测试辅助：用真实嵌入式 Strings 资源构造本地化器，初始文化可控。</summary>
internal static class LocalizerFactory
{
    public static ResourceManagerStringLocalizer Create(CultureInfo initialCulture)
        => new(new ResourceManager(
                   "KotobaSenpai.App.Resources.Strings",
                   typeof(ResourceManagerStringLocalizer).Assembly),
               initialCulture);
}
