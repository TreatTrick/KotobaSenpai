using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Platform.Windows.Overlay;
using Xunit.Abstractions;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhrasePopupViewTests(ITestOutputHelper output)
{
    [Fact]
    public Task Appending_a_sentence_retains_existing_furigana_and_underlines() => RunSta(() =>
    {
        var target = new Window { ShowActivated = false, ShowInTaskbar = false, Left = 100, Top = 100, Width = 700, Height = 400 };
        var overlay = new WpfOverlayRenderer.OverlayWindow();
        try
        {
            target.Show();
            var handle = new WindowInteropHelper(target).Handle;
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            var windowTarget = new WindowTarget(handle, "Target",
                new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            GroupedWord Word(string surface, string reading, int row) => new GroupedWord(new Token(
                surface, surface, surface, reading, reading, reading, new("noun", "", "", ""), "", "", "1", 0),
                new ScreenRect(bounds.Left + 80, bounds.Top + 60 + row * 90, 90, 40)).WithSegmentId($"s{row}-{row}");
            overlay.Render(WordOverlaySession.Start(windowTarget, [Word("僕", "ボク", 0)], underlineSegmentIds: ["s0-0"]), 1d / 3);
            var canvas = Assert.IsType<Canvas>(overlay.Content);
            var original = canvas.Children.OfType<OverlayAnnotation>().ToArray();
            Assert.Contains(original, annotation => annotation.Glyph is not null);
            Assert.Contains(original, annotation => annotation.Role == OverlayColorRole.Underline);

            overlay.Render(WordOverlaySession.Start(windowTarget,
                [Word("僕", "ボク", 0), Word("職員", "ショクイン", 1)], underlineSegmentIds: ["s0-0"]), 1d / 3);

            Assert.All(original, annotation => Assert.Contains(annotation, canvas.Children.OfType<OverlayAnnotation>()));
            Assert.True(canvas.Children.Count > original.Length);
            var secondLine = canvas.Children.OfType<OverlayAnnotation>().Except(original).ToArray();
            overlay.Render(WordOverlaySession.Start(windowTarget,
                [Word("僕", "ボク", 0), Word("職員", "ショクイン", 1)], underlineSegmentIds: ["s0-0", "s1-1"]), 1d / 3);
            Assert.All(original, annotation => Assert.Contains(annotation, canvas.Children.OfType<OverlayAnnotation>()));
            Assert.DoesNotContain(secondLine, annotation => canvas.Children.Contains(annotation));

            overlay.Render(WordOverlaySession.Start(windowTarget, []), 1d / 3);
            Assert.Empty(canvas.Children);
        }
        finally { overlay.Close(); target.Close(); }
    });

    [Fact]
    public Task Translation_mouse_gesture_is_consumed_and_toggles_once_without_activation() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var activeBefore = Native.GetActiveWindow();
            popup.ShowResult("Phrase", "Meaning", "", new ScreenRect(100, 100, 100, 30));
            var toggle = Assert.IsType<ToggleButton>(popup.FindName("TranslationButton"));
            var content = Assert.IsType<StackPanel>(popup.FindName("SentenceTranslationContent"));

            Assert.False(popup.HandleMouseInput(0x0201, false, false));
            Assert.False(popup.HandleMouseInput(0x0202, true, true));
            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            Assert.False(toggle.IsChecked);
            Assert.True(popup.HandleMouseInput(0x0202, true, true));
            Pump();
            Assert.True(toggle.IsChecked);
            Assert.Equal(Visibility.Visible, content.Visibility);
            Assert.False(popup.HandleMouseInput(0x0202, true, true));

            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            Assert.True(popup.HandleMouseInput(0x0202, true, true));
            Pump();
            Assert.False(toggle.IsChecked);
            Assert.Equal(activeBefore, Native.GetActiveWindow());

            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            Assert.True(popup.HandleMouseInput(0x0202, false, false));
            Pump();
            Assert.False(toggle.IsChecked);
        }
        finally { popup.Close(); }
    });

    [Fact]
    public Task Hiding_or_replacing_a_pressed_translation_consumes_release_without_toggling() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var anchor = new ScreenRect(100, 100, 100, 30);
            popup.ShowResult("First", "Meaning", "", anchor);
            var toggle = Assert.IsType<ToggleButton>(popup.FindName("TranslationButton"));
            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            popup.HidePopup();
            Assert.True(popup.HandleMouseInput(0x0202, false, false));
            Pump();
            popup.ShowResult("Second", "Meaning", "", anchor);
            Assert.False(toggle.IsChecked);

            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            popup.ShowResult("Third", "Meaning", "", anchor);
            Assert.True(popup.HandleMouseInput(0x0202, true, true));
            Pump();
            Assert.False(toggle.IsChecked);
            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            popup.SetSentenceTranslationEnabled(false);
            Assert.True(popup.HandleMouseInput(0x0202, true, true));
            Pump();
            Assert.False(toggle.IsChecked);

            popup.SetSentenceTranslationEnabled(true);
            Assert.True(popup.HandleMouseInput(0x0201, true, true));
            Assert.True(popup.HandleMouseInput(0x0202, true, true));
            popup.SetSentenceTranslationEnabled(false);
            popup.SetSentenceTranslationEnabled(true);
            Pump();
            Assert.False(toggle.IsChecked);
            toggle.IsEnabled = false;
            Assert.False(popup.HandleMouseInput(0x0201, true, true));
        }
        finally { popup.Close(); }
    });

    [Fact]
    public Task Intercepted_wheel_scrolls_only_the_visible_popup() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var activeBefore = Native.GetActiveWindow();
            popup.ShowResult("Long phrase", string.Join(" ", Enumerable.Repeat("Long explanation.", 1000)), "",
                new ScreenRect(100, 100, 100, 30));
            var viewer = Assert.IsType<ScrollViewer>(popup.FindName("PopupScrollViewer"));
            Assert.True(popup.HandleMouseInput(0x020A, true, false, -120));
            Pump();
            Assert.True(viewer.VerticalOffset > 0);
            Assert.False(popup.HandleMouseInput(0x020A, false, false, -120));
            Assert.False(popup.HandleMouseInput(0x0201, true, false));
            popup.HidePopup();
            Assert.False(popup.HandleMouseInput(0x020A, true, false, -120));
            Assert.Equal(activeBefore, Native.GetActiveWindow());
        }
        finally { popup.Close(); }
    });

    [Fact]
    public Task Collapsing_a_translation_above_its_source_does_not_dismiss_at_the_old_button_position() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        Native.GetCursorPos(out var originalCursor);
        try
        {
            var area = SystemParameters.WorkArea;
            var anchor = new ScreenRect((int)area.Left + 20, (int)area.Bottom - 60, (int)area.Width - 40, 30);
            popup.ShowWordMeaning(new WordMeaningView("学校", "がっこう", "Noun", "School", "Grammar"), anchor,
                new SentenceTranslation("s0", "学校に行く。", "English", true, string.Concat(Enumerable.Repeat("An extended reference translation. ", 16))));
            var toggle = Assert.IsType<ToggleButton>(popup.FindName("TranslationButton"));
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            popup.UpdateLayout();
            var pointer = toggle.PointToScreen(new Point(20, toggle.ActualHeight / 2));
            Native.SetCursorPos((int)pointer.X, (int)pointer.Y);
            Assert.True(popup.ShouldRemainOpen((int)pointer.X, (int)pointer.Y, isOverSource: false));
            toggle.IsChecked = false;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            popup.UpdateLayout();
            Assert.True(popup.PointToScreen(new Point()).Y > pointer.Y + 10);
            Assert.True(popup.ShouldRemainOpen((int)pointer.X, (int)pointer.Y, isOverSource: false));
            Assert.True(popup.ShouldRemainOpen((int)pointer.X, (int)pointer.Y + 5, isOverSource: false));
            Assert.False(popup.ShouldRemainOpen((int)pointer.X, (int)pointer.Y - 5, isOverSource: false));
        }
        finally { popup.Close(); Native.SetCursorPos(originalCursor.X, originalCursor.Y); }
    });

    [Fact]
    public Task Translation_opens_above_word_details_and_setting_changes_preserve_the_bound_result() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var word = new WordMeaningView("学校", "がっこう", "Noun", "School", "Word grammar");
            var sentence = new SentenceTranslation("s0", "学校に行く。", "English", true, "Go to school.");
            var anchor = new ScreenRect(100, 100, 100, 30);
            popup.ShowWordMeaning(word, anchor, sentence);
            var model = popup.DataContext;
            var toggle = Assert.IsType<ToggleButton>(popup.FindName("TranslationButton"));
            var translation = Assert.IsType<StackPanel>(popup.FindName("SentenceTranslationContent"));
            var section = Assert.IsType<StackPanel>(popup.FindName("SentenceTranslationSection"));
            Assert.Equal(Visibility.Collapsed, translation.Visibility);
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            popup.UpdateLayout();
            Assert.Same(model, popup.DataContext);
            Assert.Equal(Visibility.Visible, translation.Visibility);
            Assert.Contains("Go to school.", VisibleText(popup));
            var heading = Assert.IsType<TextBlock>(popup.FindName("Heading"));
            Assert.True(toggle.TranslatePoint(new Point(), popup).Y < heading.TranslatePoint(new Point(), popup).Y);
            popup.SetSentenceTranslationEnabled(false);
            Assert.Equal(Visibility.Collapsed, section.Visibility);
            Assert.Same(model, popup.DataContext);
            Assert.Contains("School", VisibleText(popup));
            popup.ShowWordMeaning(word, anchor, sentence);
            Assert.Equal(Visibility.Collapsed, section.Visibility);
            popup.SetSentenceTranslationEnabled(true);
            Assert.False(toggle.IsChecked);
            Assert.Equal(Visibility.Collapsed, translation.Visibility);
            popup.ShowWordMeaning(word, anchor, sentence with { Requested = false, Translation = null });
            Assert.Contains("next recognition", Assert.IsType<PhrasePopupViewModel>(popup.DataContext).TranslationText);
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            popup.HidePopup();
            popup.ShowWordMeaning(word, anchor, sentence);
            Assert.False(toggle.IsChecked);
        }
        finally { popup.Close(); }
    });

    [Fact]
    public Task Background_defaults_to_thirty_percent_transparency_without_fading_content() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            popup.ShowResult("Phrase", "Meaning", "", new ScreenRect(100, 100, 100, 30));
            var background = Assert.IsType<Border>(popup.FindName("PopupFrame"));
            var viewer = Assert.IsType<ScrollViewer>(popup.FindName("PopupScrollViewer"));
            Assert.Equal(0.7, background.Opacity);
            Assert.Equal(1, popup.Opacity);
            Assert.Equal(1, viewer.Opacity);
            Assert.False(background.IsAncestorOf(viewer), "Text must render outside the translucent background layer.");
        }
        finally { popup.Close(); }
    });

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.75, 0.25)]
    [InlineData(1, 0)]
    public Task Background_transparency_changes_preserve_opaque_content(double transparency, double opacity) => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            popup.ShowResult("Phrase", "Meaning", "", new ScreenRect(100, 100, 100, 30));
            var content = popup.DataContext;
            popup.ApplyAppearance(new KotobaSenpai.Core.Settings.OverlayAppearance(PopupBackgroundTransparency: transparency));

            Assert.Equal(opacity, Assert.IsType<Border>(popup.FindName("PopupFrame")).Opacity);
            Assert.Equal(1, popup.Opacity);
            Assert.Equal(1, Assert.IsType<TextBlock>(popup.FindName("Heading")).Opacity);
            Assert.Same(content, popup.DataContext);
            Assert.True(popup.IsVisible);
        }
        finally { popup.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Wrapped_sources_anchor_the_popup_to_the_hovered_line(bool phrase) => RunSta(() =>
    {
        var target = new Window { ShowActivated = false, ShowInTaskbar = false, Left = 100, Top = 100, Width = 700, Height = 600 };
        var overlay = new WpfOverlayRenderer.OverlayWindow();
        try
        {
            target.Show();
            var handle = new WindowInteropHelper(target).Handle;
            Assert.True(Native.GetWindowRect(handle, out var targetBounds));
            var first = new ScreenRect(targetBounds.Left + 100, targetBounds.Top + 50, 100, 40);
            var hovered = new ScreenRect(targetBounds.Left + 300, targetBounds.Top + 200, 100, 40);
            var word = new GroupedWord(new Token("Word", "Word", "Word", "", "", "",
                new PartsOfSpeech("Noun", "", "", ""), "", "", "", 0), [first, hovered]);
            GroupedWord Neighbor(string surface, ScreenRect rect) => new(new Token(surface, surface, surface, "", "", "",
                new PartsOfSpeech("Noun", "", "", ""), "", "", "", 0), rect);
            var neighbors = new[]
            {
                Neighbor("First neighbor", new ScreenRect(hovered.X, first.Y, 100, 40)),
                Neighbor("Second neighbor", new ScreenRect(first.X, hovered.Y, 100, 40)),
            };
            var reference = new SentenceTokenReference(0, 0, 0, 0, word.Token, [first, hovered],
                [new(0, [first]), new(1, [hovered])]);
            var groups = phrase
                ? new[] { PhraseGeometryMapper.MapGroup(new PhraseGroup("wrapped", "Type",
                    [new PhraseGroupPart([reference])], "Phrase", "Meaning", "Grammar", Guid.NewGuid())) }
                : Array.Empty<PhraseGroupView>();
            overlay.Render(WordOverlaySession.Start(new WindowTarget(handle, "Target",
                new ScreenRect(targetBounds.Left, targetBounds.Top, targetBounds.Right - targetBounds.Left, targetBounds.Bottom - targetBounds.Top)),
                [word, .. neighbors], groups), 1d / 3);

            overlay.PollHover(hovered.X + 20, hovered.Y + 10);

            var popup = Assert.Single(overlay.OwnedWindows.OfType<PhrasePopup>());
            Assert.True(popup.IsVisible);
            Assert.True(Native.GetWindowRect(new WindowInteropHelper(popup).Handle, out var bounds));
            Assert.Equal(hovered.Y, bounds.Top);
            Assert.True(bounds.Left > hovered.Right || bounds.Right < hovered.X);
            var underlines = Assert.IsType<Canvas>(overlay.Content).Children.OfType<OverlayAnnotation>().ToArray();
            Assert.Equal([OverlayColorRole.Hover, OverlayColorRole.Hover,
                OverlayColorRole.Underline, OverlayColorRole.Underline], underlines.Select(line => line.Role));

            for (var index = 0; index < neighbors.Length; index++)
            {
                overlay.StopHover();
                var rect = neighbors[index].Rects[0];
                overlay.PollHover(rect.X + 20, rect.Y + 10);
                Assert.True(popup.IsVisible);
                Assert.Equal(neighbors[index].Surface, Assert.IsType<TextBlock>(popup.FindName("Heading")).Text);
                for (var line = 0; line < underlines.Length; line++)
                    Assert.Equal(line == index + 2 ? OverlayColorRole.Hover : OverlayColorRole.Underline, underlines[line].Role);
            }

            overlay.StopHover();
            overlay.PollHover(first.X + 150, first.Y + 100);
            Assert.False(popup.IsVisible);
            Assert.All(underlines, line => Assert.Equal(OverlayColorRole.Underline, line.Role));
        }
        finally { overlay.Close(); target.Close(); }
    });

    [Fact]
    public Task Appearance_roles_share_the_live_renderer_and_edges_keep_source_alignment() => RunSta(() =>
    {
        var target = new Window { ShowActivated = false, ShowInTaskbar = false, Left = 100, Top = 100, Width = 700, Height = 500 };
        var overlay = new WpfOverlayRenderer.OverlayWindow();
        try
        {
            target.Show();
            var handle = new WindowInteropHelper(target).Handle;
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            GroupedWord Word(string surface, string reading, string accent, int x) => new(
                new Token(surface, surface, surface, reading, reading, reading,
                    new PartsOfSpeech("Noun", "", "", ""), "", "", accent, 0),
                new ScreenRect(bounds.Left + x, bounds.Top + 60, 100, 40));
            overlay.Render(WordOverlaySession.Start(new WindowTarget(handle, "Target",
                new ScreenRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)),
                [Word("語", "ゴ", "invalid", 0), Word("日本語", "ニホンゴ", "2", 150), Word("かな", "カナ", "0", 300)]), 1d / 3);
            overlay.UpdateLayout();
            var canvas = Assert.IsType<Canvas>(overlay.Content);
            var annotations = canvas.Children.OfType<OverlayAnnotation>().ToArray();
            Assert.Contains(annotations, item => item.Role == OverlayColorRole.Furigana && item.Glyph is not null);
            foreach (var role in new[] { OverlayColorRole.High, OverlayColorRole.Low })
            {
                Assert.Contains(annotations, item => item.Role == role && item.Glyph is not null);
                Assert.Contains(annotations, item => item.Role == role && item.Glyph is null);
            }
            var firstLine = annotations.First(item => item.Role == OverlayColorRole.Underline);
            Assert.Equal(0, Canvas.GetLeft(firstLine));
            var left = firstLine.PointToScreen(new Point()).X;
            Assert.InRange(left, bounds.Left - 0.5, bounds.Left + 0.5);
            Assert.True(Native.GetWindowRect(new WindowInteropHelper(overlay).Handle, out var overlayBounds));
            Assert.True(overlayBounds.Left < bounds.Left, "The native surface must reserve space for outlines outside the source bounds.");
            Assert.True(overlayBounds.Right > bounds.Right);
        }
        finally { overlay.Close(); target.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Display_binds_presentation_data_without_App_resources(bool localized) => RunSta(() =>
    {
        var popup = new PhrasePopup(localized ? new TestLocalizer() : null);
        try
        {
            var anchor = new ScreenRect(100, 100, 100, 30);
            var word = new WordMeaningView("学校", "ガッコウ", "Noun", "School", "Word grammar");
            popup.ShowResult("Phrase", "Group meaning", "Group grammar", anchor, [word], reading: "ガッコウ");
            popup.UpdateLayout();

            var phrase = Assert.IsType<PhrasePopupViewModel>(popup.DataContext);
            Assert.Contains("Phrase", VisibleText(popup));
            Assert.Contains("Group meaning", VisibleText(popup));
            Assert.Contains("School", VisibleText(popup));
            Assert.Contains("がっこう  |  gakkou", VisibleText(popup));
            var unknownPitch = localized ? "Localized Llm.WordPitchUnknown" : "pitch unknown";
            Assert.Contains(unknownPitch, VisibleText(popup));
            Assert.Contains(phrase.WordsLabel, VisibleText(popup));
            Assert.Equal(Visibility.Collapsed, Section(popup, "PosSection").Visibility);
            Assert.Equal(Visibility.Collapsed, Section(popup, "PitchSection").Visibility);
            Assert.Equal(Visibility.Visible, Section(popup, "WordsSection").Visibility);
            Assert.Equal(Visibility.Visible, Assert.IsType<Border>(popup.FindName("WordsDivider")).Visibility);

            popup.ShowWordMeaning(word, anchor);
            popup.UpdateLayout();
            var wordModel = Assert.IsType<PhrasePopupViewModel>(popup.DataContext);
            Assert.NotSame(phrase, wordModel);
            Assert.Contains("学校", VisibleText(popup));
            Assert.Contains("がっこう  |  gakkou", VisibleText(popup));
            Assert.Contains("Noun", VisibleText(popup));
            Assert.DoesNotContain(localized ? "Localized Llm.WordPosLabel" : "Pos", VisibleText(popup));
            Assert.Contains(wordModel.MeaningLabel, VisibleText(popup));
            Assert.Contains("Word grammar", VisibleText(popup));
            Assert.DoesNotContain("Group grammar", VisibleText(popup));
            Assert.Equal(Visibility.Visible, Section(popup, "PosSection").Visibility);
            Assert.Equal(Visibility.Collapsed, Section(popup, "WordsSection").Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(popup.FindName("WordsDivider")).Visibility);

            popup.ShowWordWithoutMeaning(word, anchor);
            popup.UpdateLayout();
            Assert.Contains(localized ? "Localized Llm.WordNoMeaning" : "no meaning", VisibleText(popup));
            Assert.Contains(unknownPitch, VisibleText(popup));
            Assert.DoesNotContain("Word grammar", VisibleText(popup));
            Assert.DoesNotContain("School", VisibleText(popup));
            foreach (var name in new[] { "PosSection", "MeaningSection", "GrammarSection", "WordsSection" })
                Assert.Equal(Visibility.Collapsed, Section(popup, name).Visibility);

            popup.ShowResult("Empty phrase", "", "", anchor);
            popup.UpdateLayout();
            Assert.Equal([Assert.IsType<PhrasePopupViewModel>(popup.DataContext).TranslationShowLabel, "Empty phrase"], VisibleText(popup));
        }
        finally
        {
            popup.Close();
        }
    });

    [Fact]
    public Task Known_pitch_is_superscript_and_the_metadata_cards_have_distinct_underlines() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var word = new WordMeaningView("尽きる", "ツキル", "Verb", "Run out", "",
                [new("尽きる", "ツキル", 0, 2, "[2] LH↓L")]);
            popup.ShowWordMeaning(word, new ScreenRect(100, 100, 100, 30));
            popup.UpdateLayout();
            var heading = Assert.IsType<TextBlock>(popup.FindName("Heading"));
            var badge = Assert.Single(heading.Inlines.OfType<Run>(), run => run.Text == "②");

            Assert.Equal(BaselineAlignment.Superscript, badge.BaselineAlignment);
            Assert.Contains("つきる  |  tsukiru", VisibleText(popup));
            Assert.Contains("Pitch LH↓L", VisibleText(popup));
            Assert.DoesNotContain("[2]", string.Join(" ", VisibleText(popup)));
            Assert.NotEqual(Section(popup, "PosSection").Tag, Section(popup, "PitchSection").Tag);
        }
        finally { popup.Close(); }
    });

    [Fact]
    public Task Theme_brush_changes_update_the_existing_popup() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            popup.ShowWordMeaning(new WordMeaningView("Word", "Reading", "Noun", "Meaning", ""), new ScreenRect(100, 100, 100, 30));
            var snapshot = popup.DataContext;
            var foreground = new SolidColorBrush(Colors.Black);
            var surface = new SolidColorBrush(Colors.WhiteSmoke);
            var layer = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
            popup.Resources["TextFillColorPrimaryBrush"] = foreground;
            popup.Resources["SolidBackgroundFillColorBaseBrush"] = surface;
            popup.Resources["LayerFillColorDefaultBrush"] = layer;
            popup.UpdateLayout();

            Assert.Same(foreground, Assert.IsType<TextBlock>(popup.FindName("Heading")).Foreground);
            Assert.Same(surface, Assert.IsType<Border>(popup.FindName("PopupFrame")).Background);
            Assert.Same(layer, Assert.IsType<Border>(Assert.IsType<Border>(popup.FindName("PopupFrame")).Child).Background);
            Assert.Same(snapshot, popup.DataContext);
        }
        finally
        {
            popup.Close();
        }
    });

    [Fact]
    public Task Long_headings_can_expand_beyond_the_preferred_reading_column() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            popup.ShowResult(string.Join(" ", Enumerable.Repeat("Wide heading", 12)), "Meaning", "", new ScreenRect(100, 100, 100, 30));
            Assert.True(popup.Width > 440, "The reading column is not a hard width limit.");
        }
        finally
        {
            popup.Close();
        }
    });

    [Fact]
    public Task Replacing_short_long_short_content_measures_the_current_templates() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var anchor = new ScreenRect(100, 100, 100, 30);
            var word = new WordMeaningView("Word", "", "", "", "");
            popup.ShowWordWithoutMeaning(word, anchor);
            var shortSize = new Size(popup.Width, popup.Height);
            var longText = string.Join(" ", Enumerable.Repeat("A longer explanation that wraps within the popup.", 20));
            var members = Enumerable.Range(0, 12)
                .Select(index => new WordMeaningView($"Word {index}", "", "Noun", longText, ""))
                .ToArray();

            popup.ShowResult("Long phrase", longText, longText, anchor, members);
            Assert.True(popup.Width > 360, "Long content can grow wider than the previous limit.");
            Assert.True(popup.Height > 360, "Long content can grow taller than the previous limit.");
            Assert.True(popup.Height > shortSize.Height);
            popup.UpdateLayout();
            Assert.Contains(longText, VisibleText(popup));

            popup.ShowWordWithoutMeaning(word, anchor);
            Assert.Equal(shortSize.Width, popup.Width, precision: 3);
            Assert.Equal(shortSize.Height, popup.Height, precision: 3);
            Assert.True(popup.Width > 0);
            Assert.True(popup.Height > 0);
        }
        finally
        {
            popup.Close();
        }
    });

    [Fact]
    public Task Native_window_intercepts_mouse_input_without_activation_and_keeps_registration_and_reuse() => RunSta(() =>
    {
        var target = new Window
        {
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
            Left = 100, Top = 100, Width = 600, Height = 450, Background = Brushes.White,
        };
        var popup = new PhrasePopup();
        nint handle = 0;
        try
        {
            target.Show();
            var targetHandle = new WindowInteropHelper(target).Handle;
            Assert.True(Native.GetWindowRect(targetHandle, out var targetBounds));
            var anchor = new ScreenRect(targetBounds.Left + 20, targetBounds.Top + 20, 100, 30);
            var activeBefore = Native.GetActiveWindow();

            popup.ShowResult("Phrase", "Meaning text", "", anchor);
            handle = new WindowInteropHelper(popup).Handle;
            Assert.Equal(activeBefore, Native.GetActiveWindow());
            Assert.False(popup.Topmost);
            Assert.False(popup.ShowActivated);
            Assert.False(popup.ShowInTaskbar);
            const long requiredStyles = 0x08000000 | 0x80;
            Assert.Equal(requiredStyles, Native.GetWindowLongPtr(handle, -20).ToInt64() & requiredStyles);
            Assert.Equal(0, Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x20);
            Assert.Equal((nint)3, Native.SendMessage(handle, 0x0021, targetHandle, (nint)0x02010001));
            Assert.Equal(activeBefore, Native.GetActiveWindow());
            Assert.Contains(handle, OverlayWindowRegistry.Snapshot());
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            // Isolate the native hit-test probe from unrelated desktop windows without taking focus.
            const uint positionFlags = 0x0001 | 0x0002 | 0x0010;
            try
            {
                Assert.True(Native.SetWindowPos(targetHandle, (nint)(-1), 0, 0, 0, 0, positionFlags));
                Assert.True(Native.SetWindowPos(handle, (nint)(-1), 0, 0, 0, 0, positionFlags));
                Pump();
                Assert.Equal(activeBefore, Native.GetActiveWindow());
                Assert.Equal(handle, Native.WindowFromPoint(new Native.Point { X = bounds.Left + 20, Y = bounds.Top + 20 }));
            }
            finally
            {
                Native.SetWindowPos(targetHandle, (nint)(-2), 0, 0, 0, 0, positionFlags);
                Native.SetWindowPos(handle, (nint)(-2), 0, 0, 0, 0, positionFlags);
            }
            Assert.Equal(0, Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x8);

            popup.HidePopup();
            Assert.False(popup.IsVisible);
            Assert.Contains(handle, OverlayWindowRegistry.Snapshot());
            popup.ShowWordWithoutMeaning(new WordMeaningView("Word", "", "", "", ""), anchor);
            Assert.Equal(handle, new WindowInteropHelper(popup).Handle);
            Assert.True(popup.IsVisible);

            var monitor = Native.MonitorFromWindow(targetHandle, 2);
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            Assert.True(Native.GetMonitorInfo(monitor, ref info));
            var workArea = info.WorkArea;
            var edge = new ScreenRect(workArea.Right - 10, workArea.Bottom - 10, 5, 5);
            popup.ShowResult("At edge", "Meaning text", "", edge);
            Assert.True(Native.GetWindowRect(handle, out bounds));
            Assert.True(bounds.Left >= workArea.Left && bounds.Right <= workArea.Right);
            Assert.True(bounds.Top >= workArea.Top && bounds.Bottom <= workArea.Bottom);
            Assert.True(bounds.Right <= edge.X || bounds.Bottom <= edge.Y);
            output.WriteLine("Native popup smoke: DPI={0}, work area=({1},{2})-({3},{4}).",
                Native.GetDpiForWindow(handle), workArea.Left, workArea.Top, workArea.Right, workArea.Bottom);
        }
        finally
        {
            popup.Close();
            target.Close();
        }
        Assert.DoesNotContain(handle, OverlayWindowRegistry.Snapshot());
    });

    private static ContentControl Section(PhrasePopup popup, string name)
        => Assert.IsType<ContentControl>(popup.FindName(name));

    [Fact]
    public Task Mouse_entry_between_poll_ticks_enables_the_exit_margin() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            popup.ShowResult("Phrase", "Meaning", "", new ScreenRect(100, 100, 100, 30));
            var handle = new WindowInteropHelper(popup).Handle;
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            Assert.True(popup.ShouldRemainOpen(bounds.Left - 1, bounds.Top + 20, isOverSource: true));

            popup.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = Mouse.MouseEnterEvent,
            });

            var margin = (int)Math.Floor(10 * Native.GetDpiForWindow(handle) / 96d);
            Assert.True(popup.ShouldRemainOpen(bounds.Left - margin, bounds.Top + 20, isOverSource: false));
            Assert.False(popup.ShouldRemainOpen(bounds.Left - margin - 1, bounds.Top + 20, isOverSource: false));
        }
        finally { popup.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task Hover_retains_the_popup_during_transfer_and_after_entry(bool phrase, bool longContent) => RunSta(() =>
    {
        var target = new Window { ShowActivated = false, ShowInTaskbar = false, Left = 100, Top = 100, Width = 700, Height = 600 };
        var overlay = new WpfOverlayRenderer.OverlayWindow();
        nint overlayHandle = 0;
        nint popupHandle = 0;
        try
        {
            target.Show();
            var targetHandle = new WindowInteropHelper(target).Handle;
            Assert.True(Native.GetWindowRect(targetHandle, out var targetBounds));
            var source = new ScreenRect(targetBounds.Left + 100, targetBounds.Top + 100, 100, 80);
            var word = new GroupedWord(new Token("Word", "Word", "Word", "", "", "",
                new PartsOfSpeech("Noun", "", "", ""), "", "", "", 0), source);
            var behind = new GroupedWord(new Token("Other", "Other", "Other", "", "", "",
                new PartsOfSpeech("Noun", "", "", ""), "", "", "", 0),
                new ScreenRect(source.X, source.Bottom + 10, 100, 30));
            var meaning = longContent
                ? string.Join(" ", Enumerable.Repeat("An explanation that exceeds the available monitor height.", 500))
                : "Meaning";
            var groups = phrase
                ? new[] { new PhraseGroupView(Guid.NewGuid(), "Phrase", "Type", meaning, "", 0, 1, [new([source])]) }
                : Array.Empty<PhraseGroupView>();
            overlay.Render(WordOverlaySession.Start(new WindowTarget(targetHandle, "Target",
                new ScreenRect(targetBounds.Left, targetBounds.Top, targetBounds.Right - targetBounds.Left, targetBounds.Bottom - targetBounds.Top)),
                [word, behind], groups, wordMeanings: [new WordMeaningView("Word", "", "Noun", meaning, "")]), 1d / 3);
            overlayHandle = new WindowInteropHelper(overlay).Handle;
            var popup = Assert.Single(overlay.OwnedWindows.OfType<PhrasePopup>());
            var canvas = Assert.IsType<Canvas>(overlay.Content);
            var annotations = canvas.Children.OfType<OverlayAnnotation>().ToArray();
            var positions = annotations.Select(element => (Canvas.GetLeft(element), Canvas.GetTop(element))).ToArray();
            overlay.Hide();
            overlay.ApplyAppearance(new KotobaSenpai.Core.Settings.OverlayAppearance(UnderlineColor: "#FFFFFF"));
            overlay.Show();
            overlay.PollHover(source.X + 20, source.Y + 10);
            Assert.True(popup.IsVisible);
            var snapshot = popup.DataContext;
            overlay.ApplyAppearance(new KotobaSenpai.Core.Settings.OverlayAppearance(UnderlineColor: "#000000",
                OutlineColorMode: KotobaSenpai.Core.Settings.OverlayOutlineColorMode.White, OutlineThickness: 3,
                PopupBackgroundTransparency: 0.75));
            Assert.True(popup.IsVisible);
            Assert.Same(snapshot, popup.DataContext);
            Assert.Equal(0.25, Assert.IsType<Border>(popup.FindName("PopupFrame")).Opacity);
            Assert.Equal(annotations, canvas.Children.OfType<OverlayAnnotation>());
            Assert.Equal(positions, annotations.Select(element => (Canvas.GetLeft(element), Canvas.GetTop(element))));
            Assert.Contains(annotations, element => element.Role == OverlayColorRole.Hover);
            var handle = new WindowInteropHelper(popup).Handle;
            popupHandle = handle;
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            var margin = (int)Math.Floor(10 * Native.GetDpiForWindow(handle) / 96d);

            Assert.True(bounds.Left > source.Right, "The popup must open beside the source with a gap.");
            overlay.PollHover(source.Right + 1, source.Y + 10);
            Assert.True(popup.IsVisible, "Moving across the gap toward the popup must not close it.");
            Assert.Same(snapshot, popup.DataContext);
            overlay.PollHover(bounds.Left + 20, bounds.Top + 20);
            Assert.True(popup.IsVisible);
            Assert.Same(snapshot, popup.DataContext);
            overlay.PollHover(bounds.Left + 20, bounds.Bottom + margin);
            Assert.True(popup.IsVisible, "The DPI-scaled exit margin must preserve the popup.");
            overlay.PollHover(bounds.Left + 20, bounds.Bottom + margin + 1);
            Assert.False(popup.IsVisible);

            overlay.PollHover(source.X + 20, source.Y + 10);
            Assert.True(popup.IsVisible);
            overlay.PollHover(bounds.Left + 20, bounds.Top + 20);
            var returnX = bounds.Left - margin - 1;
            Assert.InRange(returnX, source.X, source.Right);
            overlay.PollHover(returnX, source.Y + 10);
            Assert.False(popup.IsVisible, "Exiting onto the original source must not immediately reopen the popup.");
            overlay.PollHover(returnX, source.Y + 10);
            Assert.False(popup.IsVisible);
            overlay.PollHover(source.X - 10, source.Y + 10);
            overlay.PollHover(source.X + 20, source.Y + 10);
            Assert.True(popup.IsVisible);
            overlay.PollHover(source.X + 20, source.Y - 10);
            Assert.False(popup.IsVisible, "Moving away before entry must close the popup.");
            overlay.PollHover(source.X + 20, source.Y + 10);
            Assert.True(popup.IsVisible);
            overlay.ClearSession();
            Assert.False(popup.IsVisible);
        }
        finally { overlay.Close(); target.Close(); }
        Assert.DoesNotContain(overlayHandle, OverlayWindowRegistry.Snapshot());
        Assert.DoesNotContain(popupHandle, OverlayWindowRegistry.Snapshot());
    });

    [Fact]
    public Task Long_content_has_a_scrollbar_and_accepts_wheel_input_without_activation() => RunSta(() =>
    {
        var popup = new PhrasePopup();
        try
        {
            var anchor = new ScreenRect(100, 100, 100, 30);
            var longText = string.Join(" ", Enumerable.Repeat("An explanation that exceeds the available monitor height.", 500));
            var activeBefore = Native.GetActiveWindow();
            popup.ShowResult("Long phrase", longText, "", anchor);
            Pump();
            var viewer = Assert.IsType<ScrollViewer>(popup.FindName("PopupScrollViewer"));
            Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
            Assert.True(viewer.ScrollableHeight > 0);
            Assert.Equal(0, viewer.VerticalOffset);
            var focusedBefore = Keyboard.FocusedElement;
            viewer.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            });
            Assert.Same(focusedBefore, Keyboard.FocusedElement);
            Assert.Equal(activeBefore, Native.GetActiveWindow());

            var handle = new WindowInteropHelper(popup).Handle;
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            var monitor = Native.MonitorFromWindow(handle, 2);
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            Assert.True(Native.GetMonitorInfo(monitor, ref info));
            Assert.InRange(bounds.Bottom - bounds.Top, 1, (int)Math.Floor((info.WorkArea.Bottom - info.WorkArea.Top) * 2d / 3));
            var point = (nint)(((bounds.Top + 40) << 16) | ((bounds.Left + 40) & 0xffff));
            Native.SendMessage(handle, 0x020A, (nint)unchecked((int)0xFF880000), point);
            Pump();
            Assert.True(viewer.VerticalOffset > 0, "Wheel input must scroll the popup's content.");
            Assert.Equal(activeBefore, Native.GetActiveWindow());

            popup.ShowResult("Short phrase", "Short meaning", "", anchor);
            Pump();
            Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
            Assert.Equal(0, viewer.VerticalOffset);
        }
        finally { popup.Close(); }
    });

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<string> VisibleText(DependencyObject root)
    {
        if (root is UIElement { Visibility: not Visibility.Visible })
            yield break;
        if (root is TextBlock text)
            yield return text.Text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var value in VisibleText(VisualTreeHelper.GetChild(root, index)))
                yield return value;
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class TestLocalizer : IStringLocalizer
    {
        public string Get(string key, params object[] args) => $"Localized {key}";
        public event EventHandler? CultureChanged { add { } remove { } }
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo { public int Size; public Rect Monitor, WorkArea; public uint Flags; }

        [DllImport("user32.dll")]
        public static extern nint GetActiveWindow();

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern nint SendMessage(nint handle, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint handle, int index);

        [DllImport("user32.dll")]
        public static extern nint WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint handle, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(nint handle, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint handle);
    }
}
