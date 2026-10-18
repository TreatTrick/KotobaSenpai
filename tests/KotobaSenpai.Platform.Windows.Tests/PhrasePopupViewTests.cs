using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Platform.Windows.Overlay;
using Xunit.Abstractions;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhrasePopupViewTests(ITestOutputHelper output)
{
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
            popup.ShowResult("Phrase", "Group meaning", "Group grammar", anchor, [word]);
            popup.UpdateLayout();

            var phrase = Assert.IsType<PhrasePopupViewModel>(popup.DataContext);
            Assert.Contains("Phrase", VisibleText(popup));
            Assert.Contains("Group meaning", VisibleText(popup));
            Assert.Contains("School", VisibleText(popup));
            Assert.Contains(" [がっこう]", VisibleText(popup));
            var unknownPitch = localized ? "Localized Llm.WordPitchUnknown" : "pitch unknown";
            Assert.Contains($" ({unknownPitch})", VisibleText(popup));
            Assert.Contains(phrase.WordsLabel, VisibleText(popup));
            Assert.Equal(Visibility.Collapsed, Section(popup, "PosSection").Visibility);
            Assert.Equal(Visibility.Collapsed, Section(popup, "PitchSection").Visibility);
            Assert.Equal(Visibility.Visible, Section(popup, "WordsSection").Visibility);

            popup.ShowWordMeaning(word, anchor);
            popup.UpdateLayout();
            var wordModel = Assert.IsType<PhrasePopupViewModel>(popup.DataContext);
            Assert.NotSame(phrase, wordModel);
            Assert.Contains("学校 [がっこう]", VisibleText(popup));
            Assert.Contains(wordModel.PosLabel, VisibleText(popup));
            Assert.Contains(wordModel.PitchLabel, VisibleText(popup));
            Assert.Contains(wordModel.MeaningLabel, VisibleText(popup));
            Assert.Contains("Word grammar", VisibleText(popup));
            Assert.DoesNotContain("Group grammar", VisibleText(popup));
            Assert.Equal(Visibility.Visible, Section(popup, "PosSection").Visibility);
            Assert.Equal(Visibility.Collapsed, Section(popup, "WordsSection").Visibility);

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
            Assert.Equal(["Empty phrase"], VisibleText(popup));
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
            Assert.Equal(360, popup.Width);
            Assert.Equal(360, popup.Height);
            Assert.True(popup.Height > shortSize.Height);
            popup.UpdateLayout();
            Assert.Contains(longText, VisibleText(popup));

            popup.ShowWordWithoutMeaning(word, anchor);
            Assert.Equal(shortSize.Width, popup.Width, precision: 3);
            Assert.Equal(shortSize.Height, popup.Height, precision: 3);
            Assert.InRange(popup.Width, 200, 360);
            Assert.InRange(popup.Height, 1, 360);
        }
        finally
        {
            popup.Close();
        }
    });

    [Fact]
    public Task Native_window_keeps_focus_click_through_registration_and_reuse() => RunSta(() =>
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
            const long requiredStyles = 0x20 | 0x08000000 | 0x80;
            Assert.Equal(requiredStyles, Native.GetWindowLongPtr(handle, -20).ToInt64() & requiredStyles);
            Assert.Contains(handle, OverlayWindowRegistry.Snapshot());
            Assert.True(Native.GetWindowRect(handle, out var bounds));
            Assert.Equal(targetHandle, Native.WindowFromPoint(new Native.Point { X = bounds.Left + 5, Y = bounds.Top + 5 }));

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
            Assert.True(bounds.Top >= workArea.Top && bounds.Bottom <= edge.Y);
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
        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo { public int Size; public Rect Monitor, WorkArea; public uint Flags; }

        [DllImport("user32.dll")]
        public static extern nint GetActiveWindow();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint handle, int index);

        [DllImport("user32.dll")]
        public static extern nint WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint handle, out Rect rect);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(nint handle, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint handle);
    }
}
