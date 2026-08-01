namespace KotobaSenpai.Platform.Windows;

/// <summary>Windows 平台适配器抛出的可操作错误（语言包缺失、捕获失败等）。</summary>
public sealed class WindowsPlatformException(string message, Exception? inner = null)
    : Exception(message, inner);
