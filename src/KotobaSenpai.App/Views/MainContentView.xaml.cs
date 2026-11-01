using System.Windows.Controls;
using KotobaSenpai.App.Guidance.Presentation;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;

namespace KotobaSenpai.App.Views;

public partial class MainContentView : UserControl
{
    private MainInterfaceTourPresenter? _guide;

    public MainContentView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel main) return;
            if (_guide is null && main.Guidance is not null && LocalizationHost.Localizer is { } localizer)
                _guide = new MainInterfaceTourPresenter(ContentRoot, MainNavigation, RecognitionView.FindGuideTarget,
                    new TourPresentation(main.Guidance.Main, localizer));
            main.Support?.NotifyMainViewReady();
        };
        Unloaded += (_, _) => { _guide?.Dispose(); _guide = null; };
    }
}
