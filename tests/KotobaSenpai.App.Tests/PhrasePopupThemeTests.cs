using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
}
