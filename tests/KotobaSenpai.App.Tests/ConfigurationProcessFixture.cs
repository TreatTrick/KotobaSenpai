using System.IO;
using System.Windows;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.App.Tests;

/// <summary>GUI fixture for real Windows lifecycle checks; configuration executes in the test caller itself.</summary>
public static class ConfigurationProcessFixture
{
    [STAThread]
    public static int Main()
    {
        if (Environment.GetEnvironmentVariable("KOTOBA_SHORTCUT_CONFLICT_FIXTURE") == "1")
        {
            using var registrar = new KotobaSenpai.Platform.Windows.Shortcuts.Win32GlobalShortcutRegistrar(
                new KotobaSenpai.Presentation.Wpf.Threading.WpfUiThread(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            if (!KotobaSenpai.Core.Shortcuts.ShortcutGesture.TryParse("Ctrl+Alt+Shift+F11", out var gesture)
                || !registrar.TryRegister(1, gesture, out _)) return 1;
            Console.WriteLine("ready");
            Console.Out.Flush();
            Console.ReadLine();
            return 0;
        }
        if (Environment.GetEnvironmentVariable("KOTOBA_POPUP_THEME_FIXTURE") == "1")
        {
            try { PhrasePopupThemeTests.CheckDesktopPresentationAsync().GetAwaiter().GetResult(); return 0; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        var guidanceDirectory = Environment.GetEnvironmentVariable(GuidanceDesktopFixture.DirectoryEnvironmentVariable);
        if (guidanceDirectory is not null) return GuidanceDesktopFixture.Run(guidanceDirectory);
        var pickerDirectory = Environment.GetEnvironmentVariable("KOTOBA_PICKER_FIXTURE_DIRECTORY");
        if (pickerDirectory is not null)
            return WindowPickerFixture.Run(pickerDirectory, Environment.GetEnvironmentVariable("KOTOBA_PICKER_FIXTURE_MODE") ?? "host");
        var directory = Environment.GetEnvironmentVariable("KOTOBA_PROCESS_TEST_DIRECTORY");
        var lockName = Environment.GetEnvironmentVariable("KOTOBA_PROCESS_TEST_LOCK");
        if (directory is null || lockName is null)
            return 5;
        string Mode() => File.ReadAllText(Path.Combine(directory, "mode"));
        if (Mode() == "restore_failure" && File.Exists(Path.Combine(directory, "applied")))
            return 5;
        using var guiLease = ConfigurationProcessLock.TryAcquire(lockName);
        if (guiLease is null)
            return 4;
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new Window
        {
            Title = "Configuration lifecycle test", Width = 160, Height = 80,
            Left = -10000, Top = -10000, Opacity = 0, ShowActivated = false,
        };
        window.Closing += (_, e) => e.Cancel = Mode() == "ignore_close";
        window.Loaded += (_, _) => File.WriteAllText(Path.Combine(directory, "gui-ready"), Environment.ProcessId.ToString());
        window.Closed += (_, _) => File.WriteAllText(Path.Combine(directory, "gui-closed"), "closed");
        return application.Run(window);
    }
}
