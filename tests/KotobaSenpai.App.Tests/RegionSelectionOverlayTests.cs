using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using KotobaSenpai.App.Composition;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Presentation.Wpf.Overlay;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App.Tests;

public sealed partial class PhrasePopupThemeTests
{
    private static void CheckRegionSelectionOverlay()
    {
        foreach (var ending in new[] { "confirm", "cancel", "suspend", "hide", "target-close", "target-switch", "dispose" })
        {
            var target = new Window
            {
                Style = null, AllowsTransparency = false, ShowActivated = false, ShowInTaskbar = false,
                Left = -10000, Top = -10000, Width = 400, Height = 200,
            };
            target.Show();
            var tracker = new PresentationTracker();
            var snapshot = tracker.Attach(new(new WindowInteropHelper(target).Handle,
                "Region selection target", new(-10000, -10000, 400, 200)));
            var settings = new MemorySettings();
            using var selector = new RegionSelectorWindow(settings, tracker: tracker);
            var services = new ServiceCollection();
            DesktopComposition.ConfigureServices(services);
            services.AddSingleton<ISettingsService>(settings);
            services.AddSingleton<ITargetWindowTracker>(tracker);
            services.AddSingleton<IRegionSelector>(selector);
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var renderer = scope.ServiceProvider.GetRequiredService<WpfOverlayRenderer>();
            try
            {
                GroupedWord Word(string surface, string reading) => new(new Token(
                    surface, surface, surface, reading, reading, reading, new("noun", "", "", ""), "", "", "1", 0),
                    new ScreenRect(-9950, -9950, 90, 40));
                var session = WordOverlaySession.Start(snapshot.Target, [Word("僕", "ボク")]);
                renderer.Show(session);
                var overlay = Assert.Single(Application.Current.Windows.OfType<OverlayWindow>());
                var annotations = Assert.IsType<Canvas>(overlay.Content);
                Assert.Contains(annotations.Children.OfType<OverlayAnnotation>(), annotation => annotation.Glyph is not null);
                Assert.Contains(annotations.Children.OfType<OverlayAnnotation>(), annotation => annotation.Role == OverlayColorRole.Underline);
                overlay.PollHover(-9940, -9940);
                var popup = Assert.Single(overlay.OwnedWindows.OfType<PhrasePopup>());
                Assert.True(popup.IsVisible);

                selector.Flash(snapshot.Target);
                Assert.True(overlay.IsVisible);
                selector.StopFlashing();
                selector.Show(snapshot.Target);

                Assert.True(selector.IsSelecting);
                Assert.False(overlay.IsVisible, $"Region selection must hide annotations before {ending}.");
                Assert.False(popup.IsVisible, "Region selection must dismiss an existing meaning popup.");

                renderer.Show(WordOverlaySession.Start(snapshot.Target, [Word("職員", "ショクイン")]));
                Assert.False(overlay.IsVisible, "Recognition updates must stay hidden during region selection.");
                tracker.Publish(snapshot with { IsMinimized = true });
                snapshot = snapshot with { Bounds = new(-9900, -9900, 500, 250) };
                tracker.Publish(snapshot);
                renderer.RefreshAppearance();
                Assert.True(selector.IsSelecting);
                Assert.False(overlay.IsVisible, "Target restoration and appearance changes must preserve region selection.");
                Assert.False(popup.IsVisible);

                if (ending == "suspend") renderer.Suspend();
                if (ending == "hide") renderer.Hide();
                if (ending == "dispose") renderer.Dispose();
                if (ending == "target-close") tracker.Publish(snapshot with { IsValid = false });
                else if (ending == "target-switch") tracker.Publish(snapshot with { Handle = (nint)999 });
                else if (ending == "confirm")
                    Assert.Single(Assert.IsType<Canvas>(selector.Content).Children.OfType<Button>())
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else selector.Cancel();

                Assert.False(selector.IsSelecting);
                var shouldRestore = ending is "confirm" or "cancel";
                Assert.Equal(shouldRestore, overlay.IsVisible);
                Assert.False(popup.IsVisible);
                if (shouldRestore)
                {
                    overlay.PollHover(-9825, -9825);
                    Assert.True(popup.IsVisible, "Hover must resume at the updated target position after selection ends.");
                    Assert.Equal("職員", Assert.IsType<PhrasePopupViewModel>(popup.DataContext).Heading);
                }
            }
            finally
            {
                renderer.Dispose();
                target.Close();
            }
        }
    }
}
