namespace KotobaSenpai.Core.Shortcuts;

[Flags]
public enum ShortcutModifiers { None = 0, Alt = 1, Control = 2, Shift = 4 }

/// <summary>A portable key name and modifiers, independent of WPF and native key codes.</summary>
public readonly record struct ShortcutGesture(string Key, ShortcutModifiers Modifiers = ShortcutModifiers.None)
{
    private static readonly HashSet<string> NamedKeys = new(StringComparer.Ordinal)
    { "Enter", "Space", "Escape", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down" };

    public static bool TryParse(string? text, out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ShortcutModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ShortcutModifiers.Control,
                "ALT" => ShortcutModifiers.Alt,
                "SHIFT" => ShortcutModifiers.Shift,
                _ => ShortcutModifiers.None,
            };
            if (modifier == ShortcutModifiers.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }
        var key = parts[^1].ToUpperInvariant();
        var function = key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 11;
        if (function) key = "F" + int.Parse(key.AsSpan(1));
        else
        {
            if (modifiers == ShortcutModifiers.None) return false;
            if (key.Length != 1 || !char.IsAsciiLetterOrDigit(key[0]))
            {
                key = NamedKeys.FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? "";
                if (key.Length == 0) return false;
            }
        }
        gesture = new(key, modifiers);
        return true;
    }

    public override string ToString()
        => (Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl+" : "")
            + (Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt+" : "")
            + (Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift+" : "") + Key;
}
