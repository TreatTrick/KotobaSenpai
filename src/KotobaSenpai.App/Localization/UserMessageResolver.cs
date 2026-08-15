using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// Implementation of <see cref="IUserMessageResolver"/>: user-facing exceptions are translated by <see cref="IStringLocalizer"/>
/// according to their <see cref="IUserFacingException.ErrorCode"/>; other exceptions are translated by the fallback code. The raw exception text is never embedded in a template.
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
