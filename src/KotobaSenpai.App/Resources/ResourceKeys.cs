namespace KotobaSenpai.App.Resources;

/// <summary>
/// Resource-key constants for all user-visible App-layer text (XAML labels, ViewModel status text).
/// One-to-one with the keys of the neutral <c>Strings.resx</c>; the value is the resource key name. Centralizing them enables:
/// compile-time spelling checks in the ViewModel, enum validation in architecture tests, and a "no missing keys" test.
/// Keys for error-code messages are in <c>KotobaSenpai.Core.Localization.ErrorCodes</c>.
/// </summary>
public static class ResourceKeys
{
    // --- XAML labels ---
    public const string MainWindow_Title = nameof(MainWindow_Title);
    public const string Label_TargetWindow = nameof(Label_TargetWindow);
    public const string Label_Actions = nameof(Label_Actions);
    public const string Label_Language = nameof(Label_Language);
    public const string Button_RefreshWindows = nameof(Button_RefreshWindows);
    public const string Button_Recognize = nameof(Button_Recognize);
    public const string Button_Hide = nameof(Button_Hide);
    public const string Button_SetRecognitionRegion = nameof(Button_SetRecognitionRegion);
    public const string Label_Description = nameof(Label_Description);

    // --- ViewModel status text ({0} is a placeholder) ---
    public const string Status_SelectTarget = nameof(Status_SelectTarget);
    public const string Status_SelectTargetFirst = nameof(Status_SelectTargetFirst);
    public const string Status_Selected = nameof(Status_Selected);
    public const string Status_NoWindows = nameof(Status_NoWindows);
    public const string Status_WindowsFound = nameof(Status_WindowsFound);
    public const string Status_Recognizing = nameof(Status_Recognizing);
    public const string Status_NoWords = nameof(Status_NoWords);
    public const string Status_WordsRecognized = nameof(Status_WordsRecognized);
    public const string Status_Hidden = nameof(Status_Hidden);
    public const string Status_RegionSelecting = nameof(Status_RegionSelecting);
    public const string Region_Confirm = nameof(Region_Confirm);

    // --- Unexpected-error prompt (global unhandled-exception fallback dialog) ---
    public const string UnexpectedError_Title = nameof(UnexpectedError_Title);
    public const string UnexpectedError_Message = nameof(UnexpectedError_Message);

    // --- Theme mode (appearance card theme selection) ---
    public const string Label_Theme = nameof(Label_Theme);
    public const string Label_Appearance = nameof(Label_Appearance);
    public const string ThemeMode_Auto = nameof(ThemeMode_Auto);
    public const string ThemeMode_Light = nameof(ThemeMode_Light);
    public const string ThemeMode_Dark = nameof(ThemeMode_Dark);
}
