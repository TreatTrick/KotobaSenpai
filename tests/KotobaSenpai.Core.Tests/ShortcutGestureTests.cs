using KotobaSenpai.Core.Shortcuts;

namespace KotobaSenpai.Core.Tests;

public sealed class ShortcutGestureTests
{
    [Theory]
    [InlineData("f1", "F1")]
    [InlineData("alt+ctrl+r", "Ctrl+Alt+R")]
    [InlineData("Shift+F11", "Shift+F11")]
    [InlineData("ctrl+1", "Ctrl+1")]
    public void Valid_gestures_are_normalized(string input, string expected)
    {
        Assert.True(ShortcutGesture.TryParse(input, out var gesture));
        Assert.Equal(expected, gesture.ToString());
    }

    [Theory]
    [InlineData("F12")]
    [InlineData("Ctrl+F12")]
    [InlineData("R")]
    [InlineData("1")]
    [InlineData("Ctrl")]
    [InlineData("Win+R")]
    [InlineData("Ctrl+Ctrl+R")]
    [InlineData("Ctrl+")]
    public void Unsupported_gestures_are_rejected(string input)
        => Assert.False(ShortcutGesture.TryParse(input, out _));
}
