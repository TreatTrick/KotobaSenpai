using System.Windows;
using System.Windows.Input;

namespace KotobaSenpai.App.Views;

public partial class DiagnosticBundleDialog : Window
{
    private readonly bool _isConfirmation;

    internal DiagnosticBundleDialog(Window? owner, string title, string message, string details, string note,
        string question, string primaryText, string cancelText, bool isConfirmation)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        _isConfirmation = isConfirmation;
        DialogTitle.Text = title;
        MessageText.Text = message;
        DetailsText.Text = details;
        DetailsViewer.Visibility = string.IsNullOrWhiteSpace(details) ? Visibility.Collapsed : Visibility.Visible;
        NoteText.Text = note;
        NoteText.Visibility = string.IsNullOrWhiteSpace(note) ? Visibility.Collapsed : Visibility.Visible;
        QuestionText.Text = question;
        QuestionText.Visibility = string.IsNullOrWhiteSpace(question) ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = cancelText;
        CancelButton.Visibility = isConfirmation ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Content = primaryText;
        PrimaryButton.IsDefault = !isConfirmation;
        CancelButton.IsDefault = isConfirmation;
        Loaded += (_, _) => Keyboard.Focus(isConfirmation ? CancelButton : PrimaryButton);
        DialogIcon.Visibility = isConfirmation ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool ShowConfirmation(Window? owner, string title, string heading, string details,
        string note, string question, string continueText, string cancelText)
    {
        var dialog = new DiagnosticBundleDialog(owner, title, heading, details, note, question,
            continueText, cancelText, isConfirmation: true);
        return dialog.ShowDialog() == true;
    }

    public static void ShowNotice(Window? owner, string title, string message, string closeText)
    {
        var dialog = new DiagnosticBundleDialog(owner, title, message, string.Empty, string.Empty,
            string.Empty, closeText, string.Empty, isConfirmation: false);
        dialog.ShowDialog();
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        DialogResult = _isConfirmation;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnDialogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
