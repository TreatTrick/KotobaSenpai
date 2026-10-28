using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.IO;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.App.Views;
using KotobaSenpai.App.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Platform.Windows.Overlay;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KotobaSenpai.App.Tests;

public sealed class PhrasePopupThemeTests
{
    [Fact]
    public Task Open_popup_follows_application_theme_changes_without_replacing_content()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            PhrasePopup? popup = null;
            try
            {
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = ApplicationTheme.Dark });
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                popup = new PhrasePopup();
                popup.ShowWordMeaning(new WordMeaningView("Word", "", "Noun", "Meaning", ""),
                    new ScreenRect(100, 100, 100, 30));
                var content = popup.DataContext;
                var frame = Assert.IsType<Border>(popup.FindName("PopupFrame"));
                var layer = Assert.IsType<Border>(frame.Child);
                var heading = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("Heading"));
                var backgrounds = new List<Color>();

                foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark, ApplicationTheme.Light })
                {
                    ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
                    popup.UpdateLayout();

                    Assert.Same(application.FindResource("SolidBackgroundFillColorBaseBrush"), frame.Background);
                    Assert.Same(application.FindResource("LayerFillColorDefaultBrush"), layer.Background);
                    Assert.Same(application.FindResource("TextFillColorPrimaryBrush"), heading.Foreground);
                    Assert.Same(content, popup.DataContext);
                    Assert.True(popup.IsVisible);
                    backgrounds.Add(Assert.IsType<SolidColorBrush>(frame.Background).Color);
                }
                Assert.NotEqual(backgrounds[0], backgrounds[1]);
                Assert.Equal(backgrounds[0], backgrounds[2]);
                CheckAgentSettingsPresentation(application);
                CheckWindowSelectionPresentation(application);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                popup?.Close();
                application.Shutdown();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void CheckWindowSelectionPresentation(Application application)
    {
        var previousLocalizer = LocalizationHost.Localizer;
        var localizer = new ResourceManagerStringLocalizer();
        LocalizationHost.Localizer = localizer;
        try
        {
            foreach (var culture in new[] { "en", "zh-CN" })
            foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
            foreach (var width in new[] { 420, 660 })
            {
                localizer.ApplyCulture(CultureInfo.GetCultureInfo(culture));
                ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
                var page = new RecognitionPage
                {
                    DataContext = new
                    {
                        HasPreview = true, HasPreviewError = false, TargetTitle = "Preview target", TargetStatus = "Selected",
                        PreviewError = string.Empty, SelectionButtonText = localizer.Get("WindowPicker_Select"),
                        SelectWindowCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                        RetryPreviewCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                    },
                };
                var image = Assert.IsType<System.Windows.Controls.Image>(page.FindName("TargetPreviewImage"));
                var source = BitmapSource.Create(320, 180, 96, 96, PixelFormats.Bgra32, null,
                    Enumerable.Repeat((byte)255, 320 * 180 * 4).ToArray(), 320 * 4);
                image.Source = source;
                var frame = new Border { Child = page, Background = (Brush)application.FindResource("SolidBackgroundFillColorBaseBrush") };
                TextElement.SetForeground(frame, (Brush)application.FindResource("TextFillColorPrimaryBrush"));
                var host = new Window { Content = frame, Width = width, Height = 720, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    host.Show();
                    var settled = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    timer.Tick += (_, _) => { timer.Stop(); settled.Continue = false; };
                    timer.Start();
                    Dispatcher.PushFrame(settled);
                    image.Source = source;
                    host.UpdateLayout();
                    Assert.Same(source, image.Source);
                    Assert.True(image.IsVisible);
                    var card = Assert.IsType<KotobaSenpai.App.Controls.SettingsCard>(page.FindName("TargetWindowCard"));
                    Assert.Same(application.FindResource("CardBackgroundFillColorDefaultBrush"), card.Background);
                    Assert.Equal("Preview target", System.Windows.Automation.AutomationProperties.GetName(card));
                    Assert.Equal("Preview target", System.Windows.Automation.AutomationProperties.GetName(image));
                    Assert.Equal(Stretch.Uniform, image.Stretch);
                    var preview = Assert.IsType<Border>(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(image)));
                    Assert.Equal(144, preview.ActualWidth);
                    Assert.Equal(preview.ActualWidth, preview.ActualHeight);
                    Assert.Same(application.FindResource("LayerFillColorDefaultBrush"), preview.Background);
                    Assert.Empty(Descendants(page).OfType<ComboBox>());
                    var select = Assert.IsAssignableFrom<System.Windows.Controls.Button>(page.FindName("SelectWindowButton"));
                    Assert.Equal(localizer.Get("WindowPicker_Select"), select.Content);
                    Assert.Equal(select.Content, System.Windows.Automation.AutomationProperties.GetName(select));
                    Assert.True(select.TranslatePoint(new Point(select.ActualWidth, 0), page).X <= page.ActualWidth);
                    var bitmap = new RenderTargetBitmap(width, 720, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(frame);
                    var previewDirectory = Path.Combine(AppContext.BaseDirectory, "previews");
                    Directory.CreateDirectory(previewDirectory);
                    using var file = File.Create(Path.Combine(previewDirectory, $"window-picker-{culture}-{theme}-{width}.png"));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(file);
                }
                finally { host.Close(); }
            }
        }
        finally { LocalizationHost.Localizer = previousLocalizer; }
    }

    private static void CheckAgentSettingsPresentation(Application application)
    {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/KotobaSenpai.Platform.Windows;component/Presentation/FluentDesignTokens.xaml", UriKind.Relative) });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/KotobaSenpai.App;component/Themes/SettingsStyles.xaml", UriKind.Relative) });
        var previousLocalizer = LocalizationHost.Localizer;
        var localizer = new ResourceManagerStringLocalizer();
        LocalizationHost.Localizer = localizer;
        var settings = new MemorySettings();
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://api.example.com/v1");
        settings.SetValue(LlmSettingsKeys.ApiKey, "preview-test-key");
        settings.SetValue(LlmSettingsKeys.Model, "preview-model");
        var language = new LanguageService(localizer, new LocalAppDataLanguagePreferenceStore(settings), applyGlobalCulture: _ => { }, settings: settings);
        var detector = new PreviewDetector();
        var catalog = new EmptyCatalog();
        using var llm = new LlmSettingsViewModel(settings, catalog, localizer, new LlmConfigurationService(settings, detector, catalog));
        using var viewModel = new SettingsPageViewModel(settings, new LocalAppDataThemePreferenceStore(settings), language, llm, localizer, language,
            new OverlayAppearanceViewModel(settings, localizer));
        try
        {
            foreach (var culture in new[] { "en", "zh-CN" })
            foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
            foreach (var failed in new[] { false, true })
            {
                language.ChangeCulture(new CultureInfo(culture));
                ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
                detector.Fail = failed;
                llm.CheckConfigurationCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                var page = new SettingsPage { DataContext = viewModel };
                var advanced = Assert.IsType<System.Windows.Controls.Expander>(page.FindName("AdvancedLlmSettings"));
                Assert.True(advanced.IsExpanded);
                var frame = new Border { Background = (Brush)application.FindResource("SolidBackgroundFillColorBaseBrush"), Child = page };
                TextElement.SetForeground(frame, (Brush)application.FindResource("TextFillColorPrimaryBrush"));
                var preview = new Window
                {
                    Content = frame, Width = 1000, Height = 800, Left = -10000, Top = -10000,
                    WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
                };
                try
                {
                    preview.Show();
                    var loaded = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    timer.Tick += (_, _) => { timer.Stop(); loaded.Continue = false; };
                    timer.Start();
                    Dispatcher.PushFrame(loaded);
                    frame.UpdateLayout();
                    CheckOverlayAppearancePresentation(application, page, frame, viewModel, culture, theme);
                    var content = Assert.IsAssignableFrom<FrameworkElement>(advanced.Content);
                    Assert.True(content.IsVisible);
                    Assert.True(content.ActualHeight > 0);
                    var copy = Assert.IsAssignableFrom<System.Windows.Controls.Button>(page.FindName("CopyAgentPromptButton"));
                    Assert.Equal(localizer.Get("Agent_CopyPrompt"), copy.Content);
                    Assert.True(copy.ActualWidth > 0);
                    llm.ReportPromptCopy(true);
                    frame.UpdateLayout();
                    var copyStatus = Assert.IsType<System.Windows.Controls.TextBlock>(page.FindName("AgentPromptCopyStatusText"));
                    var copyStatusIcon = Assert.IsType<SymbolIcon>(page.FindName("AgentPromptCopyStatusIcon"));
                    var agentCard = Assert.IsType<KotobaSenpai.App.Controls.SettingsCard>(page.FindName("AgentApiConfigurationCard"));
                    Assert.True(agentCard.IsAncestorOf(copyStatus));
                    Assert.Equal(localizer.Get("Agent_PromptCopied"), copyStatus.Text);
                    Assert.Equal(SymbolRegular.CheckmarkCircle24, copyStatusIcon.Symbol);
                    Assert.Same(application.FindResource("SystemFillColorSuccessBrush"), copyStatusIcon.Foreground);
                    var description = Assert.IsType<System.Windows.Controls.TextBlock>(agentCard.Template.FindName("CardDescription", agentCard));
                    Assert.True(copyStatus.TranslatePoint(new Point(0, 0), agentCard).Y
                        >= description.TranslatePoint(new Point(0, description.ActualHeight), agentCard).Y);
                    llm.ReportPromptCopy(false);
                    frame.UpdateLayout();
                    Assert.Equal(localizer.Get("Agent_CopyFailed"), copyStatus.Text);
                    Assert.Equal(SymbolRegular.Warning24, copyStatusIcon.Symbol);
                    Assert.Same(application.FindResource("SystemFillColorCriticalBrush"), copyStatusIcon.Foreground);
                    llm.ReportPromptCopy(true);
                    frame.UpdateLayout();
                    var check = Assert.IsAssignableFrom<System.Windows.Controls.Button>(page.FindName("CheckConfigurationButton"));
                    var checkCard = Assert.IsType<Card>(page.FindName("ConfigurationCheckCard"));
                    Assert.True(checkCard.IsAncestorOf(check));
                    Assert.True(checkCard.IsAncestorOf(Assert.IsType<System.Windows.Controls.TextBlock>(page.FindName("ConfigurationOperationStatusText"))));
                    Assert.True(checkCard.IsAncestorOf(Assert.IsType<System.Windows.Controls.TextBlock>(page.FindName("ConfigurationErrorDetailsText"))));
                    Assert.Equal(localizer.Get("Agent_CheckConfiguration"),
                        Assert.IsType<System.Windows.Controls.TextBlock>(page.FindName("CheckConfigurationLabel")).Text);
                    var label = Assert.IsType<System.Windows.Controls.TextBlock>(page.FindName("ConfigurationValidationText"));
                    Assert.Equal(localizer.Get(failed ? "Agent_ConfigurationUnavailable" : "Agent_ConfigurationUsable"), label.Text);
                    Assert.Same(application.FindResource(failed ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush"), label.Foreground);
                    var success = Assert.IsType<SymbolIcon>(page.FindName("ConfigurationSuccessIcon"));
                    var error = Assert.IsType<SymbolIcon>(page.FindName("ConfigurationErrorIcon"));
                    Assert.Equal(!failed, success.IsVisible);
                    Assert.Equal(failed, error.IsVisible);
                    var copyError = Assert.IsAssignableFrom<System.Windows.Controls.Button>(page.FindName("CopyConfigurationErrorButton"));
                    Assert.Equal(failed, copyError.IsVisible);
                    Assert.True(check.TranslatePoint(new Point(check.ActualWidth, 0), page).X
                        < label.TranslatePoint(new Point(0, 0), page).X);
                    if (failed)
                    {
                        Assert.True(label.TranslatePoint(new Point(label.ActualWidth, 0), page).X
                            < copyError.TranslatePoint(new Point(0, 0), page).X);
                        Assert.DoesNotContain("preview-test-key", llm.ConfigurationErrorDetails);
                    }
                    var scroll = Assert.IsType<ScrollViewer>(page.FindName("SettingsScrollViewer"));
                    scroll.ScrollToVerticalOffset(agentCard.TranslatePoint(new Point(0, 0), (UIElement)scroll.Content).Y);
                    frame.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(1000, 800, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(frame);
                    var previews = Path.Combine(AppContext.BaseDirectory, "previews");
                    Directory.CreateDirectory(previews);
                    using var file = File.Create(Path.Combine(previews, $"agent-settings-{culture}-{theme}-{(failed ? "error" : "success")}.png"));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(file);

                    detector.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var checking = llm.CheckConfigurationCommand.ExecuteAsync(null);
                    frame.UpdateLayout();
                    var ring = Assert.IsType<ProgressRing>(page.FindName("ConfigurationProgressRing"));
                    Assert.True(ring.IsVisible);
                    Assert.True(ring.IsIndeterminate);
                    Assert.False(check.IsEnabled);
                    detector.Pending.SetResult(new(null, "cancelled"));
                    var completed = new DispatcherFrame();
                    var completionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    completionTimer.Tick += (_, _) =>
                    {
                        if (checking.IsCompleted && check.IsEnabled || DateTime.UtcNow >= deadline)
                        { completionTimer.Stop(); completed.Continue = false; }
                    };
                    completionTimer.Start();
                    Dispatcher.PushFrame(completed);
                    checking.GetAwaiter().GetResult();
                    detector.Pending = null;
                    frame.UpdateLayout();
                    Assert.False(ring.IsVisible);
                    Assert.False(ring.IsIndeterminate);
                    Assert.True(check.IsEnabled);
                    Assert.Equal(localizer.Get("Llm_FetchCancelled"), llm.ConfigurationStatus);
                }
                finally { preview.Close(); }
            }
        }
        finally { LocalizationHost.Localizer = previousLocalizer; }
    }

    private static void CheckOverlayAppearancePresentation(Application application, SettingsPage page, Border frame,
        SettingsPageViewModel viewModel, string culture, ApplicationTheme theme)
    {
        var items = Assert.IsType<ItemsControl>(page.FindName("OverlayColorSettings"));
        Assert.Equal(4, items.Items.Count);
        var container = Assert.IsType<ContentPresenter>(items.ItemContainerGenerator.ContainerFromIndex(1));
        var picker = Assert.Single(Descendants(container).OfType<OverlayColorPicker>());
        Assert.Same(viewModel.OverlayAppearance, picker.Settings);
        var choice = Assert.IsType<OverlayColorChoice>(picker.DataContext);
        var choose = Assert.IsAssignableFrom<System.Windows.Controls.Button>(picker.FindName("ChooseColorButton"));
        Assert.Equal(choice.Label, System.Windows.Automation.AutomationProperties.GetName(choose));
        var popup = Assert.IsType<Popup>(picker.FindName("PickerPopup"));
        var hex = Assert.IsType<System.Windows.Controls.TextBox>(picker.FindName("HexInput"));
        var confirm = Assert.IsAssignableFrom<System.Windows.Controls.Button>(picker.FindName("ConfirmColorButton"));
        var cancel = Assert.IsAssignableFrom<System.Windows.Controls.Button>(picker.FindName("CancelColorButton"));
        var original = choice.Value;

        choose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.True(popup.IsOpen);
        Assert.Equal(original, hex.Text);
        page.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var spectrum = Assert.IsType<ColorSpectrum>(picker.FindName("Spectrum"));
        var hue = Assert.IsType<Slider>(picker.FindName("HueSlider"));
        Assert.True(spectrum.ActualWidth > 0);
        hue.Value = 120;
        spectrum.SelectAt(new Point(spectrum.ActualWidth, 0));
        Assert.Equal("#00FF00", hex.Text);
        Assert.Equal(original, choice.Value);
        spectrum.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(spectrum), 0, Key.Left)
            { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.NotEqual("#00FF00", hex.Text);
        Assert.Equal(120, spectrum.Hue);
        hue.Value = 240;
        spectrum.SelectAt(new Point(spectrum.ActualWidth, 0));
        Assert.Equal("#0000FF", hex.Text);
        var pickerFrame = Assert.IsType<Border>(popup.Child);
        pickerFrame.UpdateLayout();
        var pickerImage = new RenderTargetBitmap((int)Math.Ceiling(pickerFrame.ActualWidth), (int)Math.Ceiling(pickerFrame.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        pickerImage.Render(pickerFrame);
        var pickerDirectory = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(pickerDirectory);
        using (var pickerFile = File.Create(Path.Combine(pickerDirectory, $"color-picker-{culture}-{theme}.png")))
        {
            var pickerEncoder = new PngBitmapEncoder();
            pickerEncoder.Frames.Add(BitmapFrame.Create(pickerImage));
            pickerEncoder.Save(pickerFile);
        }
        hex.Text = "#000000";
        Assert.Equal(original, choice.Value);
        cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.False(popup.IsOpen);
        Assert.Equal(original, choice.Value);

        choose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        hex.Text = "#556677";
        hex.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(hex), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.False(popup.IsOpen);
        Assert.Equal(original, choice.Value);

        choose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        hex.Text = "#123";
        confirm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.True(popup.IsOpen);
        Assert.NotEmpty(choice.ErrorMessage);
        Assert.Equal(original, choice.Value);
        spectrum.Color = Colors.Black;
        hex.Text = "#123";
        spectrum.SelectAt(new Point(spectrum.ActualWidth, spectrum.ActualHeight));
        Assert.Equal("#000000", hex.Text);
        hex.Text = "#abcdef";
        hex.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(hex), 0, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.False(popup.IsOpen);
        Assert.Equal("#ABCDEF", choice.Value);
        var preview = Assert.IsType<OverlayAppearancePreview>(page.FindName("OverlayPreview"));
        Assert.Equal(viewModel.OverlayAppearance.Appearance, preview.Appearance);
        Assert.NotEmpty(preview.Children.OfType<OverlayAnnotation>());
        var outline = Assert.IsType<ToggleSwitch>(page.FindName("OverlayOutlineSwitch"));
        var outlineMode = Assert.IsType<ComboBox>(page.FindName("OverlayOutlineColorModeComboBox"));
        var outlineThickness = Assert.IsType<Slider>(page.FindName("OverlayOutlineThicknessSlider"));
        Assert.Equal(3, outlineMode.Items.Count);
        outlineMode.SelectedValue = OverlayOutlineColorMode.White;
        outlineThickness.Value = 2.25;
        frame.UpdateLayout();
        Assert.Equal(OverlayOutlineColorMode.White, preview.Appearance.OutlineColorMode);
        Assert.Equal(2.25, preview.Appearance.OutlineThickness);
        outline.IsChecked = false;
        frame.UpdateLayout();
        Assert.False(outlineMode.IsEnabled);
        Assert.False(outlineThickness.IsEnabled);
        viewModel.OverlayAppearance.ResetColorsCommand.Execute(null);
        Assert.False(viewModel.OverlayAppearance.OutlineEnabled);
        Assert.Equal(OverlayOutlineColorMode.White, viewModel.OverlayAppearance.OutlineColorMode);
        Assert.Equal(2.25, viewModel.OverlayAppearance.OutlineThickness);
        Assert.Equal("#FF4444", choice.Value);
        outline.IsChecked = true;
        outlineMode.SelectedValue = OverlayOutlineColorMode.Auto;
        outlineThickness.Value = 1;

        var scroll = Assert.IsType<ScrollViewer>(page.FindName("SettingsScrollViewer"));
        scroll.ScrollToVerticalOffset(((FrameworkElement)page.FindName("OverlayOutlineCard")).TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 40);
        var settled = new DispatcherFrame();
        var settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        settleTimer.Tick += (_, _) => { settleTimer.Stop(); settled.Continue = false; };
        settleTimer.Start();
        Dispatcher.PushFrame(settled);
        frame.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        frame.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1000, 800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var directory = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, $"overlay-appearance-{culture}-{theme}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(file);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private sealed class MemorySettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }

    private sealed class EmptyCatalog : ILlmModelCatalog
    {
        public Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(LlmModelCatalogResult.Success([]));
    }

    private sealed class PreviewDetector : ILlmConnectionDetector
    {
        public Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default)
            => DetectAsync(configuration, cancellationToken);
        public bool Fail { get; set; }
        public TaskCompletionSource<LlmDetectionResult>? Pending { get; set; }
        public Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default)
            => Pending?.Task ?? Task.FromResult(Fail
                ? new LlmDetectionResult(null, "authentication_failed", "HTTP 401: the provider rejected preview-test-key.")
                : new LlmDetectionResult(candidate));
    }
}
