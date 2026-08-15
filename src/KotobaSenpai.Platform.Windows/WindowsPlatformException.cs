using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Platform.Windows;

/// <summary>
/// Actionable error thrown by the Windows platform adapter (missing language pack, capture failure, etc.). Carries a
/// stable <see cref="ErrorCode"/> (for the presentation layer to translate) and implements the user-facing exception
/// marker interface; <c>message</c> is developer-readable text only and is not shown directly to the user.
/// </summary>
public sealed class WindowsPlatformException : Exception, IUserFacingException
{
    public string ErrorCode { get; }

    public WindowsPlatformException(string errorCode, string message, Exception? inner = null)
        : base(message, inner)
        => ErrorCode = errorCode;
}
