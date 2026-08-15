using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// Thrown when an aggregate invariant is violated. It carries a stable <see cref="ErrorCode"/> (for the
/// presentation layer to translate) and implements the user-facing exception marker interface;
/// <see cref="Message"/> is developer-readable text only and is not shown directly to users.
/// </summary>
public sealed class BusinessRuleValidationException : Exception, IUserFacingException
{
    public string ErrorCode { get; }

    public string Details { get; }

    public BusinessRuleValidationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        Details = message;
    }

    public override string ToString() => $"{GetType().FullName}: {Message}";
}