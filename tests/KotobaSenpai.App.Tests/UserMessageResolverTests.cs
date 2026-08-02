using System.Globalization;
using KotobaSenpai.App.Localization;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.SeedWork;
using KotobaSenpai.Platform.Windows;

namespace KotobaSenpai.App.Tests;

public sealed class UserMessageResolverTests
{
    private readonly UserMessageResolver _resolver = new(LocalizerFactory.Create(new CultureInfo("zh-CN")));

    [Fact]
    public void Maps_ocr_language_pack_missing_code_to_localized_message()
    {
        var ex = new WindowsPlatformException(ErrorCodes.OcrLanguagePackMissing, "dev detail");

        Assert.Equal("未找到日语 OCR 语言包，请在 Windows 设置中安装日语语言包后重试。", _resolver.Resolve(ex, ErrorCodes.RecognitionFailed));
    }

    [Fact]
    public void Maps_overlay_target_not_specified_code_via_business_rule_exception()
    {
        var ex = new BusinessRuleValidationException(new FakeRule(ErrorCodes.OverlayTargetNotSpecified));

        Assert.Equal("覆盖层会话必须指定目标窗口。", _resolver.Resolve(ex, ErrorCodes.RecognitionFailed));
    }

    [Fact]
    public void Maps_frame_pixel_data_too_short_code_via_invalid_frame_exception()
    {
        var ex = new InvalidFrameException(ErrorCodes.FramePixelDataTooShort, "bgra32", "dev detail");

        Assert.Equal("帧像素数据长度不足。", _resolver.Resolve(ex, ErrorCodes.RecognitionFailed));
    }

    [Fact]
    public void Uses_fallback_code_for_non_user_facing_exception_and_never_embeds_raw_message()
    {
        var ex = new Exception("raw internal detail");

        Assert.Equal("窗口枚举失败。", _resolver.Resolve(ex, ErrorCodes.WindowEnumerationFailed));
        Assert.Equal("识别失败。", _resolver.Resolve(ex, ErrorCodes.RecognitionFailed));
        Assert.DoesNotContain("raw internal detail", _resolver.Resolve(ex, ErrorCodes.WindowEnumerationFailed));
    }

    [Fact]
    public void User_facing_error_code_takes_precedence_over_fallback()
    {
        var ex = new WindowsPlatformException(ErrorCodes.OcrLanguagePackMissing, "dev detail");

        var message = _resolver.Resolve(ex, ErrorCodes.RecognitionFailed);

        Assert.Equal("未找到日语 OCR 语言包，请在 Windows 设置中安装日语语言包后重试。", message);
    }

    private sealed class FakeRule : IBusinessRule
    {
        public FakeRule(string errorCode) => ErrorCode = errorCode;
        public string Message => "test rule";
        public string ErrorCode { get; }
        public bool IsBroken() => true;
    }
}
