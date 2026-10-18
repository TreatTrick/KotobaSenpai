namespace KotobaSenpai.Core.Errors;

/// <summary>
/// Stable, locale-neutral user-visible error codes. Core/Platform exceptions carry these codes rather than
/// localized text; the App's presentation layer translates them into localized user messages by code. Each code
/// value maps one-to-one to a resource key in the neutral <c>Strings.resx</c>.
/// </summary>
public static class ErrorCodes
{
    /// <summary>meikiocr local model missing (MeikiOcrWordRecognizer/MeikiOcrEngine).</summary>
    public const string OcrModelMissing = nameof(OcrModelMissing);

    /// <summary>meikiocr inference failed (MeikiOcrWordRecognizer).</summary>
    public const string OcrInferenceFailed = nameof(OcrInferenceFailed);

    /// <summary>Overlay session has no target window specified (OverlayTargetMustBeSpecifiedRule).</summary>
    public const string OverlayTargetNotSpecified = nameof(OverlayTargetNotSpecified);

    /// <summary>Capture frame pixel data is too short (CapturedFrame).</summary>
    public const string FramePixelDataTooShort = nameof(FramePixelDataTooShort);

    /// <summary>Window enumeration failed (fallback code when the ViewModel refreshes).</summary>
    public const string WindowEnumerationFailed = nameof(WindowEnumerationFailed);

    /// <summary>Recognition failed (fallback code when the ViewModel recognizes).</summary>
    public const string RecognitionFailed = nameof(RecognitionFailed);

    /// <summary>Selected target is invalid, hidden, minimized, or has no client area.</summary>
    public const string TargetWindowUnavailable = nameof(TargetWindowUnavailable);

    /// <summary>Windows Graphics Capture is unavailable on this device.</summary>
    public const string WindowCaptureNotSupported = nameof(WindowCaptureNotSupported);

    /// <summary>The selected window could not be captured.</summary>
    public const string WindowCaptureFailed = nameof(WindowCaptureFailed);

    /// <summary>No valid frame arrived before the capture deadline.</summary>
    public const string WindowCaptureTimedOut = nameof(WindowCaptureTimedOut);

    /// <summary>UniDic dictionary missing (UniDicTokenizer / UniDicDictionaryInstaller).</summary>
    public const string UniDicDictionaryMissing = nameof(UniDicDictionaryMissing);

    /// <summary>UniDic dictionary present but its version/format/integrity is invalid (UniDicTokenizer / UniDicDictionaryInstaller).</summary>
    public const string UniDicDictionaryInvalid = nameof(UniDicDictionaryInvalid);

    /// <summary>UniDic dictionary download/extraction failed (UniDicDictionaryInstaller).</summary>
    public const string UniDicDownloadFailed = nameof(UniDicDownloadFailed);
}
