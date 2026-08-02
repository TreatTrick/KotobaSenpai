using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// 管理应用 UI 语言：解析启动文化（持久化偏好 -> 系统中文则简体中文，否则英文），
/// 设置全局 <c>CurrentUICulture</c> 与 <see cref="ResourceManagerStringLocalizer"/>，
/// 持久化/恢复偏好，并向视图暴露 <see cref="AvailableCultures"/> 与 <see cref="CurrentCulture"/> 供绑定。
/// 默认 UI：系统为中文则简体中文，否则英文；英文为中性回退。
/// </summary>
public sealed partial class LanguageService : ObservableObject
{
    /// <summary>支持的文化：默认简体中文在前，英文为回退。</summary>
    public IReadOnlyList<CultureInfo> AvailableCultures { get; } = [new CultureInfo("zh-CN"), new CultureInfo("en")];

    private readonly ResourceManagerStringLocalizer _localizer;
    private readonly ILanguagePreferenceStore _store;
    private readonly Func<CultureInfo> _osCultureProvider;
    private readonly Action<CultureInfo> _applyGlobalCulture;
    private bool _applying;

    [ObservableProperty]
    private CultureInfo _currentCulture;

    public LanguageService(
        ResourceManagerStringLocalizer localizer,
        ILanguagePreferenceStore store,
        Func<CultureInfo>? osCultureProvider = null,
        Action<CultureInfo>? applyGlobalCulture = null)
    {
        _localizer = localizer;
        _store = store;
        _osCultureProvider = osCultureProvider ?? (() => CultureInfo.CurrentUICulture);
        _applyGlobalCulture = applyGlobalCulture ?? ApplyGlobalCultureDefault;
        _currentCulture = AvailableCultures[0];
    }

    private static void ApplyGlobalCultureDefault(CultureInfo culture)
    {
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>启动时解析并应用文化（不持久化，仅恢复或取默认）。</summary>
    public void Initialize()
    {
        SetCulture(ResolveStartupCulture(), persist: false);
    }

    /// <summary>运行时切换文化：应用全局与本地化器，并持久化用户选择。</summary>
    public void ChangeCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        SetCulture(culture, persist: true);
    }

    /// <summary>ComboBox 双向绑定设置 CurrentCulture 时触发应用与持久化。</summary>
    partial void OnCurrentCultureChanged(CultureInfo value)
    {
        if (_applying)
            return;
        SetCulture(value, persist: true);
    }

    private void SetCulture(CultureInfo culture, bool persist)
    {
        _applying = true;
        try
        {
            _applyGlobalCulture(culture);
            _localizer.ApplyCulture(culture);

            if (CurrentCulture.Name != culture.Name)
                CurrentCulture = culture;

            if (persist)
                _store.Save(culture.Name);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>解析启动文化：持久化偏好 -> 系统中文则简体中文，否则英文。</summary>
    private CultureInfo ResolveStartupCulture()
    {
        var persisted = _store.Load();
        if (persisted is not null && TryMatchSupported(persisted, out var persistedCulture))
            return persistedCulture;

        // 无持久化偏好（首次启动）时：系统 UI 文化为中文则默认简体中文，否则默认英文。
        return IsChinese(_osCultureProvider()) ? AvailableCultures[0] : AvailableCultures[1];
    }

    private static bool IsChinese(CultureInfo culture)
        => string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>按 TwoLetterISOLanguageName 匹配支持的文化（en* -> en, zh* -> zh-CN）。</summary>
    private bool TryMatchSupported(CultureInfo culture, out CultureInfo matched)
    {
        foreach (var supported in AvailableCultures)
        {
            if (string.Equals(supported.TwoLetterISOLanguageName, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            {
                matched = supported;
                return true;
            }
        }

        matched = AvailableCultures[0];
        return false;
    }

    /// <summary>按 culture 名匹配支持的文化；非法名不抛异常而是返回 false。</summary>
    private bool TryMatchSupported(string cultureName, out CultureInfo matched)
    {
        try
        {
            return TryMatchSupported(new CultureInfo(cultureName), out matched);
        }
        catch (CultureNotFoundException)
        {
            matched = AvailableCultures[0];
            return false;
        }
    }
}
