using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using KotobaSenpai.App.Support;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Views.Support;

public partial class SupportWindow : Window
{
    private readonly SupportService _support;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger _logger;
    private bool _showCodes;
    private bool _closing;
    private string _payment = "payPal";
    private string? _feedbackKey;
    private static bool Animate => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    public SupportWindow(SupportService support, IStringLocalizer localizer, ILogger logger)
    {
        _support = support;
        _localizer = localizer;
        _logger = logger;
        InitializeComponent();
        _showCodes = !support.IsSupported;
        Mascot.IsSupported = support.IsSupported;
        _support.PropertyChanged += OnSupportChanged;
        _localizer.CultureChanged += OnCultureChanged;
        Closed += (_, _) =>
        {
            _support.PropertyChanged -= OnSupportChanged;
            _localizer.CultureChanged -= OnCultureChanged;
        };
        if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh") WeChat.IsChecked = true;
        else PayPal.IsChecked = true;
        UpdatePresentation();
    }

    public void ShowSupport(Window owner, bool manually)
    {
        Owner = owner;
        ShowActivated = manually;
        Opacity = 0;
        Show();
        UpdateLayout();
        SupportWindows.Position(this, owner, centerOnOwner: manually);
        if (Animate)
        {
            var top = Top;
            BeginAnimation(TopProperty, new DoubleAnimation(top + 24, top, TimeSpan.FromSeconds(0.35))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)));
        }
        else Opacity = 1;
    }

    public void ActivateCentered()
    {
        BeginAnimation(TopProperty, null);
        SupportWindows.Position(this, Owner, centerOnOwner: true);
        Activate();
    }

    private void OnCultureChanged(object? sender, EventArgs e) => UpdatePresentation();
    private void OnSupportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SupportService.IsSupported))
        {
            if (Mascot.IsSupported != _support.IsSupported) _showCodes = !_support.IsSupported;
            Mascot.IsSupported = _support.IsSupported;
        }
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        HeaderTitle.Text = _localizer.Get(_support.IsSupported ? "support.thanks.title" : "support.title");
        UsageText.Text = _localizer.Get("support.usage", _support.State.RecognitionCount);
        Subtitle.Text = _localizer.Get(_support.IsSupported ? "support.confirmed.detail" : "support.subtitle");
        AlternateSubtitle.Text = _localizer.Get(_support.IsSupported ? "support.subtitle" : "support.confirmed.detail");
        PaymentCaption.Text = _localizer.Get($"payment.{_payment}.caption");
        ConfirmButton.Content = _localizer.Get(_support.IsSupported ? "support.back" : "support.already");
        ConfirmButton.Background = _support.IsSupported ? Brushes.Transparent : (Brush)FindResource("AccentFillColorDefaultBrush");
        PaymentCard.Visibility = _showCodes ? Visibility.Visible : Visibility.Hidden;
        ThanksCard.Visibility = _showCodes ? Visibility.Hidden : Visibility.Visible;
        PayPalLink.Visibility = _payment == "payPal" ? Visibility.Visible : Visibility.Collapsed;
        var paymentName = _payment == "payPal" ? "PayPal" : _localizer.Get($"payment.{_payment}");
        System.Windows.Automation.AutomationProperties.SetName(QrImage, _localizer.Get("support.qr", paymentName));
        LinkFeedback.Text = !string.IsNullOrEmpty(_support.Error) ? _support.Error : _feedbackKey is null ? string.Empty : _localizer.Get(_feedbackKey);
    }

    private void OnPaymentChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string payment } || QrImage is null) return;
        _payment = payment;
        try
        {
            var filename = payment == "payPal" ? "paypal.png" : payment + ".jpg";
            QrImage.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Support", filename)));
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to load payment image."); }
        UpdatePresentation();
    }

    private void OnShowCodes(object sender, RoutedEventArgs e) { _showCodes = true; UpdatePresentation(); }
    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_support.IsSupported) _showCodes = false;
        else if (_support.ConfirmSupport()) { _showCodes = false; _feedbackKey = null; }
        UpdatePresentation();
    }

    private void OnGitHub(object sender, RoutedEventArgs e)
    {
        if (!OpenLink(SupportService.GitHubLink)) return;
        if (_support.RecordGitHubVisit()) { Mascot.Celebrate(); _feedbackKey = "support.github.opened"; }
        UpdatePresentation();
    }

    private void OnPayPal(object sender, RoutedEventArgs e)
    {
        if (OpenLink(SupportService.PayPalLink)) _feedbackKey = "support.paypal.opened";
        UpdatePresentation();
    }

    private bool OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open support link.");
            _feedbackKey = "support.link.failed";
            UpdatePresentation();
            return false;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseAnimated();
    }
    private void OnClose(object sender, RoutedEventArgs e) => CloseAnimated();
    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        if (!Animate) { Close(); return; }
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.2));
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }
}
