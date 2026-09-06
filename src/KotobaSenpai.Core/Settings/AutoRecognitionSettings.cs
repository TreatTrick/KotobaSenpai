namespace KotobaSenpai.Core.Settings;

public static class AutoRecognitionSettings
{
    public const string EnabledKey = "AutoRecognitionEnabled";
    public const string IntervalMsKey = "AutoRecognitionIntervalMs";
    public const string DifferenceThresholdKey = "AutoRecognitionDifferenceThreshold";

    public const int DefaultIntervalMs = 300;
    public const int MinimumIntervalMs = 100;
    public const double DefaultDifferenceThreshold = 0.01;
}
