using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Controls;

/// <summary>A draft-only Fluent color editor; closing the popup never commits a color.</summary>
public partial class OverlayColorPicker : UserControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings),
        typeof(OverlayAppearanceViewModel), typeof(OverlayColorPicker));
    public OverlayAppearanceViewModel? Settings
    {
        get => (OverlayAppearanceViewModel?)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }
    private bool _updating;

    public OverlayColorPicker()
    {
        InitializeComponent();
        Unloaded += (_, _) => PickerPopup.IsOpen = false;
    }

    private void ChooseColor_Click(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = !PickerPopup.IsOpen;
    private void Picker_Opened(object? sender, EventArgs e)
    {
        if (DataContext is not OverlayColorChoice choice)
            return;
        choice.SetError(null);
        HexInput.Text = choice.Value;
        Spectrum.Focus();
    }

    private void HexInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updating || !OverlayAppearanceSettings.TryNormalizeColor(HexInput.Text, out var hex))
            return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        _updating = true;
        Spectrum.Color = color;
        HueSlider.Value = Spectrum.Hue;
        DraftSwatch.Background = new SolidColorBrush(color);
        _updating = false;
    }

    private void Hue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || Spectrum is null)
            return;
        Spectrum.Hue = e.NewValue;
    }

    private void Spectrum_ColorChanged(object? sender, EventArgs e)
    {
        if (_updating || HexInput is null)
            return;
        _updating = true;
        var color = Spectrum.Color;
        HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        HueSlider.Value = Spectrum.Hue;
        DraftSwatch.Background = new SolidColorBrush(color);
        _updating = false;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();
    private void Confirm()
    {
        if (DataContext is OverlayColorChoice choice && Settings?.TrySaveColor(choice, HexInput.Text) == true)
        {
            PickerPopup.IsOpen = false;
            ChooseColorButton.Focus();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();
    private void Cancel()
    {
        if (DataContext is OverlayColorChoice choice)
            choice.SetError(null);
        PickerPopup.IsOpen = false;
        ChooseColorButton.Focus();
    }

    private void Picker_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        else if (e.Key == Key.Enter && e.OriginalSource is TextBox or ColorSpectrum or Slider) { Confirm(); e.Handled = true; }
    }
}
