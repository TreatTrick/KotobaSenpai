using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Shortcuts;

namespace KotobaSenpai.App.Controls;

/// <summary>Records a gesture while existing global bindings are released; commits only after key release.</summary>
public sealed class ShortcutCaptureButton : Wpf.Ui.Controls.Button
{
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(nameof(Entry),
        typeof(ShortcutEntryViewModel), typeof(ShortcutCaptureButton));
    public ShortcutEntryViewModel? Entry
    {
        get => (ShortcutEntryViewModel?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    private readonly DispatcherTimer _releaseTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private ShortcutEntryViewModel? _recording;
    private ShortcutGesture? _candidate;
    private Key _pressedKey;
    private bool _invalid;

    public ShortcutCaptureButton()
    {
        _releaseTimer.Tick += (_, _) => FinishWhenReleased();
        Unloaded += (_, _) => CancelRecording();
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (_recording is not null || Entry?.BeginRecording() != true) return;
        _recording = Entry;
        _candidate = null;
        _invalid = false;
        _pressedKey = Key.None;
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_recording is null) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        if (e.IsRepeat || _releaseTimer.IsEnabled) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        _pressedKey = key;
        if (key != Key.Escape)
        {
            var modifiers = Keyboard.Modifiers;
            var name = key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString();
            var raw = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "")
                + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "")
                + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + name;
            if (ShortcutGesture.TryParse(raw, out var gesture)) _candidate = gesture;
            else _invalid = true;
        }
        _releaseTimer.Start();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (_recording is not null) { e.Handled = true; if (_releaseTimer.IsEnabled) FinishWhenReleased(); }
        else base.OnPreviewKeyUp(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        CancelRecording();
    }

    private void CancelRecording()
    {
        if (_recording is null) return;
        _candidate = null;
        _invalid = false;
        _releaseTimer.Start();
        FinishWhenReleased();
    }

    private void FinishWhenReleased()
    {
        if (_recording is null || Keyboard.Modifiers != ModifierKeys.None
            || (_pressedKey != Key.None && Keyboard.IsKeyDown(_pressedKey))) return;
        var entry = _recording;
        _recording = null;
        _releaseTimer.Stop();
        entry.FinishRecording(_candidate, _invalid);
    }
}
