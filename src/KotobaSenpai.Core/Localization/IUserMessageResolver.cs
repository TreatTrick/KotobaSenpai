namespace KotobaSenpai.Core.Localization;

/// <summary>
/// Presentation-layer port: maps an exception to a localized user message. If the exception is an
/// <see cref="IUserFacingException"/>, it is translated by its <c>ErrorCode</c>; otherwise the caller-provided
/// fallback error code is used. The raw exception text is never shown as a translated message. The port lives
/// in Core (depending only on the BCL <see cref="Exception"/>); the implementation lives in App.
/// </summary>
public interface IUserMessageResolver
{
    /// <summary>Resolves an exception to a localized user message: when <paramref name="exception"/> is a user-visible exception it is translated by its error code, otherwise by <paramref name="fallbackErrorCode"/>.</summary>
    string Resolve(Exception exception, string fallbackErrorCode);
}
