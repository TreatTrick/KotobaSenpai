using KotobaSenpai.Core.Errors;

namespace KotobaSenpai.Core.Updates;

/// <summary>Carries a stable localized error code while retaining diagnostic details for logs.</summary>
public sealed class AppUpdateException(string errorCode, string message, Exception? innerException = null)
    : Exception(message, innerException), IUserFacingException
{
    public string ErrorCode { get; } = errorCode;
}
