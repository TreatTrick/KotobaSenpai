using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// Domain exception thrown when capture frame data is invalid. It derives from <see cref="ArgumentException"/>
/// to preserve the parameter-name semantics, while also carrying a stable <see cref="ErrorCode"/> and
/// implementing the user-visible exception marker interface; the concrete wording is localized by the
/// presentation layer by code.
/// </summary>
public sealed class InvalidFrameException : ArgumentException, IUserFacingException
{
    public string ErrorCode { get; }

    public InvalidFrameException(string errorCode, string paramName, string message)
        : base(message, paramName)
        => ErrorCode = errorCode;
}
