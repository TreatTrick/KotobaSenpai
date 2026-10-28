using KotobaSenpai.Core.Errors;
namespace KotobaSenpai.App.Resources;

/// <summary>
/// Resource-key constants for all user-visible App-layer text (XAML labels, ViewModel status text).
/// One-to-one with the keys of the neutral <c>Strings.resx</c>; the value is the resource key name. Centralizing them enables:
/// compile-time spelling checks in the ViewModel, enum validation in architecture tests, and a "no missing keys" test.
/// Keys for error-code messages are in <c>KotobaSenpai.Core.Localization.ErrorCodes</c>.
/// </summary>
public static class ResourceKeys
{
    public const string WindowPicker_Select = nameof(WindowPicker_Select);
    public const string WindowPicker_Reselect = nameof(WindowPicker_Reselect);
    public const string WindowPicker_Retry = nameof(WindowPicker_Retry);
    public const string WindowPicker_Hint = nameof(WindowPicker_Hint);
    public const string WindowPicker_Empty = nameof(WindowPicker_Empty);
    public const string WindowPicker_Untitled = nameof(WindowPicker_Untitled);
    public const string WindowPicker_EmptyHint = nameof(WindowPicker_EmptyHint);
    public const string WindowPicker_Selected = nameof(WindowPicker_Selected);
    public const string WindowPicker_Picking = nameof(WindowPicker_Picking);
    public const string WindowPicker_Loading = nameof(WindowPicker_Loading);
    public const string WindowPicker_Unavailable = nameof(WindowPicker_Unavailable);
    public const string WindowPicker_Closed = nameof(WindowPicker_Closed);
    public const string WindowPicker_PreviewFailed = nameof(WindowPicker_PreviewFailed);

    public const string OverlayAppearance_OutlineColorMode = nameof(OverlayAppearance_OutlineColorMode);
    public const string OverlayAppearance_OutlineColorModeDescription = nameof(OverlayAppearance_OutlineColorModeDescription);
    public const string OverlayAppearance_OutlineAuto = nameof(OverlayAppearance_OutlineAuto);
    public const string OverlayAppearance_OutlineBlack = nameof(OverlayAppearance_OutlineBlack);
    public const string OverlayAppearance_OutlineWhite = nameof(OverlayAppearance_OutlineWhite);
    public const string OverlayAppearance_OutlineThickness = nameof(OverlayAppearance_OutlineThickness);
    public const string OverlayAppearance_OutlineThicknessDescription = nameof(OverlayAppearance_OutlineThicknessDescription);
    public const string OverlayAppearance_Title = nameof(OverlayAppearance_Title);
    public const string OverlayFuriganaColor = nameof(OverlayFuriganaColor);
    public const string OverlayFuriganaColor_Description = nameof(OverlayFuriganaColor_Description);
    public const string OverlayPitchHighColor = nameof(OverlayPitchHighColor);
    public const string OverlayPitchHighColor_Description = nameof(OverlayPitchHighColor_Description);
    public const string OverlayPitchLowColor = nameof(OverlayPitchLowColor);
    public const string OverlayPitchLowColor_Description = nameof(OverlayPitchLowColor_Description);
    public const string OverlayUnderlineColor = nameof(OverlayUnderlineColor);
    public const string OverlayUnderlineColor_Description = nameof(OverlayUnderlineColor_Description);
    public const string OverlayAppearance_Outline = nameof(OverlayAppearance_Outline);
    public const string OverlayAppearance_OutlineDescription = nameof(OverlayAppearance_OutlineDescription);
    public const string OverlayAppearance_Preview = nameof(OverlayAppearance_Preview);
    public const string OverlayAppearance_PreviewDescription = nameof(OverlayAppearance_PreviewDescription);
    public const string OverlayAppearance_ResetColors = nameof(OverlayAppearance_ResetColors);
    public const string OverlayAppearance_Hex = nameof(OverlayAppearance_Hex);
    public const string OverlayAppearance_Hue = nameof(OverlayAppearance_Hue);
    public const string OverlayAppearance_Spectrum = nameof(OverlayAppearance_Spectrum);
    public const string OverlayAppearance_SpectrumHelp = nameof(OverlayAppearance_SpectrumHelp);
    public const string OverlayAppearance_Confirm = nameof(OverlayAppearance_Confirm);
    public const string OverlayAppearance_Cancel = nameof(OverlayAppearance_Cancel);
    public const string OverlayAppearance_SaveFailed = nameof(OverlayAppearance_SaveFailed);
    public const string OverlayAppearance_InvalidColor = nameof(OverlayAppearance_InvalidColor);
    public const string OverlayAppearance_ColorsReset = nameof(OverlayAppearance_ColorsReset);
    // --- XAML labels ---
    public const string MainWindow_Title = nameof(MainWindow_Title);
    public const string Label_TargetWindow = nameof(Label_TargetWindow);
    public const string Label_Actions = nameof(Label_Actions);
    public const string Label_Language = nameof(Label_Language);
    public const string Label_ExplanationLanguage = nameof(Label_ExplanationLanguage);
    public const string Description_ExplanationLanguage = nameof(Description_ExplanationLanguage);
    public const string Button_SaveExplanationLanguage = nameof(Button_SaveExplanationLanguage);
    public const string ExplanationLanguage_Current = nameof(ExplanationLanguage_Current);
    public const string ExplanationLanguage_Saved = nameof(ExplanationLanguage_Saved);
    public const string ExplanationLanguage_Invalid = nameof(ExplanationLanguage_Invalid);
    public const string ExplanationLanguage_SaveFailed = nameof(ExplanationLanguage_SaveFailed);
    public const string Button_Recognize = nameof(Button_Recognize);
    public const string Button_Hide = nameof(Button_Hide);
    public const string Button_SetRecognitionRegion = nameof(Button_SetRecognitionRegion);
    public const string Label_AutoRecognition = nameof(Label_AutoRecognition);
    public const string Label_Description = nameof(Label_Description);
    public const string Label_LlmSettings = nameof(Label_LlmSettings);
    public const string Label_LlmProtocol = nameof(Label_LlmProtocol);
    public const string Label_LlmEndpoint = nameof(Label_LlmEndpoint);
    public const string Label_LlmApiKey = nameof(Label_LlmApiKey);
    public const string Label_LlmModel = nameof(Label_LlmModel);
    public const string Button_LlmFetchModels = nameof(Button_LlmFetchModels);
    public const string Button_LlmToggleKey = nameof(Button_LlmToggleKey);
    public const string Llm_ProtocolOpenAiChat = nameof(Llm_ProtocolOpenAiChat);
    public const string Llm_ProtocolOpenAiResponses = nameof(Llm_ProtocolOpenAiResponses);
    public const string Llm_ProtocolAnthropic = nameof(Llm_ProtocolAnthropic);
    public const string Llm_EndpointRequired = nameof(Llm_EndpointRequired);
    public const string Llm_ApiKeyRequired = nameof(Llm_ApiKeyRequired);
    public const string Llm_LoadingModels = nameof(Llm_LoadingModels);
    public const string Llm_NoModels = nameof(Llm_NoModels);
    public const string Llm_ModelsLoaded = nameof(Llm_ModelsLoaded);
    public const string Llm_AuthenticationFailed = nameof(Llm_AuthenticationFailed);
    public const string Llm_Timeout = nameof(Llm_Timeout);
    public const string Llm_CatalogUnavailable = nameof(Llm_CatalogUnavailable);
    public const string Llm_FetchFailed = nameof(Llm_FetchFailed);
    public const string Llm_FetchCancelled = nameof(Llm_FetchCancelled);

    // --- ViewModel status text ({0} is a placeholder) ---
    public const string Status_SelectTarget = nameof(Status_SelectTarget);
    public const string Status_SelectTargetFirst = nameof(Status_SelectTargetFirst);
    public const string Status_Selected = nameof(Status_Selected);
    public const string Status_Recognizing = nameof(Status_Recognizing);
    public const string Status_NoWords = nameof(Status_NoWords);
    public const string Status_WordsRecognized = nameof(Status_WordsRecognized);
    public const string Status_Hidden = nameof(Status_Hidden);
    public const string Status_RegionSelecting = nameof(Status_RegionSelecting);
    public const string Region_Confirm = nameof(Region_Confirm);
    public const string Region_NoJapanese = nameof(Region_NoJapanese);

    // --- Unexpected-error prompt (global unhandled-exception fallback dialog) ---
    public const string UnexpectedError_Title = nameof(UnexpectedError_Title);
    public const string UnexpectedError_Message = nameof(UnexpectedError_Message);

    // --- Theme mode (appearance card theme selection) ---
    public const string Label_Theme = nameof(Label_Theme);
    public const string Label_Appearance = nameof(Label_Appearance);
    public const string Label_FuriganaSize = nameof(Label_FuriganaSize);
    public const string ThemeMode_Auto = nameof(ThemeMode_Auto);
    public const string ThemeMode_Light = nameof(ThemeMode_Light);
    public const string ThemeMode_Dark = nameof(ThemeMode_Dark);
}
