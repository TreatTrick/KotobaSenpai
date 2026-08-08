namespace KotobaSenpai.Core.Localization;

/// <summary>
/// 稳定、locale 无关的用户可见错误码。Core/Platform 异常携带这些码而非本地化文本；
/// App 表现层按码翻译为本地化用户消息。码值与中性 <c>Strings.resx</c> 的资源键一一对应。
/// </summary>
public static class ErrorCodes
{
    /// <summary>meikiocr 本地模型缺失（MeikiOcrWordRecognizer/MeikiOcrEngine）。</summary>
    public const string OcrModelMissing = nameof(OcrModelMissing);

    /// <summary>meikiocr 推理失败（MeikiOcrWordRecognizer）。</summary>
    public const string OcrInferenceFailed = nameof(OcrInferenceFailed);

    /// <summary>覆盖层会话未指定目标窗口（OverlayTargetMustBeSpecifiedRule）。</summary>
    public const string OverlayTargetNotSpecified = nameof(OverlayTargetNotSpecified);

    /// <summary>捕获帧像素数据长度不足（CapturedFrame）。</summary>
    public const string FramePixelDataTooShort = nameof(FramePixelDataTooShort);

    /// <summary>窗口枚举失败（ViewModel 刷新时的回退码）。</summary>
    public const string WindowEnumerationFailed = nameof(WindowEnumerationFailed);

    /// <summary>识别失败（ViewModel 识别时的回退码）。</summary>
    public const string RecognitionFailed = nameof(RecognitionFailed);

    /// <summary>UniDic 词典缺失（UniDicTokenizer / UniDicDictionaryInstaller）。</summary>
    public const string UniDicDictionaryMissing = nameof(UniDicDictionaryMissing);

    /// <summary>UniDic 词典存在但版本/格式/完整性无效（UniDicTokenizer / UniDicDictionaryInstaller）。</summary>
    public const string UniDicDictionaryInvalid = nameof(UniDicDictionaryInvalid);

    /// <summary>UniDic 词典下载/解压失败（UniDicDictionaryInstaller）。</summary>
    public const string UniDicDownloadFailed = nameof(UniDicDownloadFailed);
}
