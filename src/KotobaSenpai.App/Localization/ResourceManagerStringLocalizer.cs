using System.Globalization;
using System.Resources;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="IStringLocalizer"/> 的 BCL 实现：用 <see cref="ResourceManager"/> 解析嵌入式 .resx，
/// 自带 culture 回退链（active -> 中性英文）。本地维护当前文化，文化切换时触发
/// <see cref="CultureChanged"/>；不直接改全局 <c>CurrentUICulture</c>（由 <see cref="LanguageService"/> 负责）。
/// </summary>
public sealed class ResourceManagerStringLocalizer : IStringLocalizer
{
    /// <summary>嵌入式资源的基础名（根命名空间 + Resources 文件夹 + Strings）。</summary>
    private const string ResourceBaseName = "KotobaSenpai.App.Resources.Strings";

    private readonly ResourceManager _manager;
    private CultureInfo _culture;

    /// <summary>生产构造：从本程序集加载 <c>Strings</c> 资源，初始文化取当前 UI 文化。</summary>
    public ResourceManagerStringLocalizer()
        : this(new ResourceManager(ResourceBaseName, typeof(ResourceManagerStringLocalizer).Assembly),
               CultureInfo.CurrentUICulture)
    {
    }

    /// <summary>测试构造：注入自定义 <see cref="ResourceManager"/> 与初始文化。</summary>
    public ResourceManagerStringLocalizer(ResourceManager manager, CultureInfo initialCulture)
    {
        _manager = manager;
        _culture = initialCulture;
    }

    public event EventHandler? CultureChanged;

    /// <summary>本地维护的当前 UI 文化（与全局 CurrentUICulture 由 LanguageService 保持同步）。</summary>
    public CultureInfo CurrentCulture => _culture;

    /// <inheritdoc />
    public string Get(string key, params object[] args)
    {
        var value = _manager.GetString(key, _culture) ?? key;
        return args.Length == 0 ? value : string.Format(_culture, value, args);
    }

    /// <summary>切换本地文化；若实际改变则触发 <see cref="CultureChanged"/>，使订阅者即时刷新。</summary>
    public void ApplyCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (culture.Name == _culture.Name)
            return;

        _culture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
