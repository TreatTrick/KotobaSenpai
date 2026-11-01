using System.IO;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class CaptureBorderPreferencesTests
{
    [Fact]
    public void Changes_notify_capture_consumers_after_persistence_and_survive_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ks_capture_border_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.SetValue("UnrelatedSetting", "preserved");
            var preferences = new CaptureBorderPreferences(settings);
            var changes = new List<bool>();
            preferences.Changed += (_, _) =>
            {
                var restored = new CaptureBorderPreferences(new SettingsService(path));
                Assert.Equal(preferences.ShowBorder, restored.ShowBorder);
                changes.Add(preferences.ShowBorder);
            };

            Assert.False(preferences.ShowBorder);
            preferences.ShowBorder = true;
            preferences.ShowBorder = true;
            preferences.ShowBorder = false;
            preferences.ShowBorder = false;

            Assert.Equal(new[] { true, false }, changes);
            Assert.Equal("preserved", new SettingsService(path).GetValue("UnrelatedSetting"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
