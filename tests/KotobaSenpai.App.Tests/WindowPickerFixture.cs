using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.App.Views;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Platform.Windows.Capture;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.App.Tests;

/// <summary>Opt-in desktop fixture: real picking, tracking and capture, with no OCR models or provider requests.</summary>
internal static class WindowPickerFixture
{
    internal static int Run(string directory, string mode)
    {
        Directory.CreateDirectory(directory);
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (mode != "host") return RunTarget(application, directory, mode);
        application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Light });
        application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        foreach (var path in new[]
        {
            "/KotobaSenpai.Platform.Windows;component/Presentation/FluentDesignTokens.xaml",
            "/KotobaSenpai.App;component/Themes/SettingsStyles.xaml",
        }) application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
        var localizer = new ResourceManagerStringLocalizer();
        LocalizationHost.Localizer = localizer;
        var logger = new FixtureLogger(directory);
        using var tracker = new WinEventTargetWindowTracker(logger);
        using var capture = new WgcWindowFrameCapture(tracker);
        var workflow = new WordOverlayApplicationService(new Recognizer(),
            new WordGroupingService(new Tokenizer(), new SentenceSegmenter(), new SpanResolver()),
            new Renderer(), KotobaSenpai.TestSupport.PhraseAnalysisStub.Create());
        using var vm = new RecognitionPageViewModel(new Win32WindowPicker(localizer, logger), workflow,
            new RegionSelectorWindow(new Settings(), localizer, tracker), new Settings(), localizer,
            new UserMessageResolver(localizer), logger, tracker, capture: capture);
        var page = new RecognitionPage { DataContext = vm };
        var host = new Window { Title = "Window picker host", Width = 660, Height = 760, Left = 620, Top = 60, Content = page };
        var previews = 0;
        void Save() => File.WriteAllText(Path.Combine(directory, "host-state.json"), JsonSerializer.Serialize(new
        {
            selected = vm.SelectedWindow?.Handle.ToInt64(), title = vm.TargetTitle, status = vm.TargetStatus,
            picking = vm.IsPickingWindow, busy = vm.IsSelectingWindow, preview = vm.HasPreview,
            failed = vm.HasPreviewError, version = vm.SelectionVersion, previews,
        }));
        vm.PropertyChanged += (_, _) => Save();
        vm.PreviewReady += (_, _) => { previews++; Save(); };
        host.Loaded += (_, _) => Save();
        return application.Run(host);
    }

    private static int RunTarget(Application application, string directory, string mode)
    {
        var clicks = 0;
        var button = new Button { Content = "Target clicks: 0", FontSize = 28, Background = Brushes.CornflowerBlue };
        var window = new Window
        {
            Title = "Window picker target", Width = 480, Height = 300, Left = 70, Top = 90,
            Content = button, WindowStyle = mode == "borderless" ? WindowStyle.None : WindowStyle.SingleBorderWindow,
        };
        void Save() => File.WriteAllText(Path.Combine(directory, "target-state.json"), JsonSerializer.Serialize(new
        {
            handle = new WindowInteropHelper(window).Handle.ToInt64(), clicks, style = mode,
        }));
        button.Click += (_, _) => { button.Content = $"Target clicks: {++clicks}"; Save(); };
        window.Loaded += (_, _) => Save();
        return application.Run(window);
    }

    private sealed class FixtureLogger(string directory) : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (level >= LogLevel.Error) File.AppendAllText(Path.Combine(directory, "errors.txt"), $"{message}: {exception}\n");
        }
    }

    private sealed class Settings : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => null;
        public override void SetValue(string key, string? value) { }
    }
    private sealed class Recognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target,
            CancellationToken cancellationToken = default, ScreenRect? region = null)
            => throw new InvalidOperationException("The picker fixture must not invoke recognition.");
    }
    private sealed class Tokenizer : ITokenizer { public IReadOnlyList<Token> Tokenize(string? text) => []; }
    private sealed class SpanResolver : ITokenSpanResolver { public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens) => []; }
    private sealed class Renderer : IOverlayRenderer
    {
        public void Show(WordOverlaySession session, Func<bool>? canRender = null) { }
        public void Hide() { }
    }
}
