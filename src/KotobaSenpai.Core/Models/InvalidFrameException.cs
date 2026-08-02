using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// 捕获帧数据不合法时抛出的领域异常。派生自 <see cref="ArgumentException"/> 以保留参数名语义，
/// 同时携带稳定 <see cref="ErrorCode"/> 并实现用户可见异常标记接口；具体文案由表现层按码本地化。
/// </summary>
public sealed class InvalidFrameException : ArgumentException, IUserFacingException
{
    public string ErrorCode { get; }

    public InvalidFrameException(string errorCode, string paramName, string message)
        : base(message, paramName)
        => ErrorCode = errorCode;
}
