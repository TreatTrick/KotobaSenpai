using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="IUserMessageResolver"/> 实现：用户可见异常按其 <see cref="IUserFacingException.ErrorCode"/>
/// 经 <see cref="IStringLocalizer"/> 翻译；其余异常按回退码翻译。原始异常文本不被嵌入模板。
/// </summary>
public sealed class UserMessageResolver : IUserMessageResolver
{
    private readonly IStringLocalizer _localizer;

    public UserMessageResolver(IStringLocalizer localizer)
        => _localizer = localizer;

    /// <inheritdoc />
    public string Resolve(Exception exception, string fallbackErrorCode)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var errorCode = (exception as IUserFacingException)?.ErrorCode ?? fallbackErrorCode;
        return _localizer.Get(errorCode);
    }
}
