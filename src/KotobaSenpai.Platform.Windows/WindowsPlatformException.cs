using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Platform.Windows;

/// <summary>
/// Windows 平台适配器抛出的可操作错误（语言包缺失、捕获失败等）。
/// 携带稳定 <see cref="ErrorCode"/>（供表现层翻译）并实现用户可见异常标记接口；
/// <c>message</c> 仅为开发者可读文本，不直接展示给用户。
/// </summary>
public sealed class WindowsPlatformException : Exception, IUserFacingException
{
    public string ErrorCode { get; }

    public WindowsPlatformException(string errorCode, string message, Exception? inner = null)
        : base(message, inner)
        => ErrorCode = errorCode;
}
