namespace KotobaSenpai.Core.Localization;

/// <summary>
/// Marker interface for user-visible exceptions. Exceptions in Core/Platform whose message could reach the UI
/// implement this interface and expose a stable <see cref="ErrorCode"/>; the App's presentation layer
/// <c>IUserMessageResolver</c> translates the code into a localized message rather than showing the raw
/// exception text as translated text.
/// </summary>
public interface IUserFacingException
{
    /// <summary>Stable error code corresponding to an <c>ErrorCodes</c> key, never null.</summary>
    string ErrorCode { get; }
}
