using System.IO;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Support;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.TestSupport;

namespace KotobaSenpai.App.Tests;

public sealed class SupportServiceTests
{
    private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0);

    [Fact]
    public void Usage_counts_each_completion_but_only_distinct_local_days_and_waits_for_restart()
    {
        var settings = new MemorySettings();
        using var support = Create(settings);
        for (var day = 0; day < 3; day++)
        {
            support.RecordRecognition(Noon.AddDays(day));
            support.RecordRecognition(Noon.AddDays(day));
        }
        Assert.Equal(6, support.State.RecognitionCount);
        Assert.Equal(3, support.State.ActiveDays.Length);
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(2), true));
        using var restarted = Create(settings);
        Assert.True(restarted.TryMarkStartupPrompt(Noon.AddDays(2), true));
        Assert.False(restarted.TryMarkStartupPrompt(Noon.AddDays(2), true));
        using var again = Create(settings);
        Assert.False(again.TryMarkStartupPrompt(Noon.AddDays(2), true));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(18, true)]
    [InlineData(19, false)]
    public void Startup_is_checked_once_and_only_in_daytime(int hour, bool expected)
    {
        var settings = WithDays(8);
        using var support = Create(settings);
        var now = Noon.AddDays(8).Date.AddHours(hour);
        Assert.Equal(expected, support.TryMarkStartupPrompt(now, true));
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(9), true));
        Assert.Equal(expected ? 7 : 0, support.State.LastPromptMilestone);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recognition_or_blocked_startup_defers_until_another_process(bool cancel)
    {
        var settings = WithDays(3);
        using var support = Create(settings);
        if (cancel) support.CancelStartupReminder();
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(3), cancel));
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(4), true));
        using var restarted = Create(settings);
        Assert.True(restarted.TryMarkStartupPrompt(Noon.AddDays(4), true));
    }

    [Fact]
    public void Saving_failure_does_not_publish_usage_confirmation_or_prompt()
    {
        var settings = WithDays(3);
        using var support = Create(settings);
        var previous = support.State;
        settings.FailWrites = true;
        support.RecordRecognition(Noon.AddDays(3));
        Assert.False(support.ConfirmSupport());
#if DEBUG
        Assert.False(support.ResetForDebugging());
#endif
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(3), true));
        Assert.Same(previous, support.State);
        Assert.NotEmpty(support.Error);
    }

    [Fact]
    public void GitHub_stops_reminders_without_claiming_a_tip_and_confirmation_persists()
    {
        var settings = WithDays(3);
        using var support = Create(settings);
        Assert.True(support.RecordGitHubVisit());
        Assert.False(support.State.HasTipped);
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(3), true));
        Assert.True(support.ConfirmSupport());
        using var restarted = Create(settings);
        Assert.True(restarted.State.HasTipped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("broken")]
    [InlineData("{\"ActiveDays\":null}")]
    [InlineData("{\"Version\":999}")]
    public void Missing_or_corrupt_state_defaults_without_touching_other_settings(string? raw)
    {
        var settings = new MemorySettings { Raw = raw };
        using var support = Create(settings);
        Assert.Equal(0, support.State.RecognitionCount);
        Assert.Empty(support.State.ActiveDays);
    }

    [Fact]
    public void Dates_cross_midnight_are_deduplicated_and_storage_is_bounded()
    {
        var settings = WithDays(40);
        using var support = Create(settings);
        Assert.Equal(30, support.State.ActiveDays.Length);
        support.RecordRecognition(Noon.AddDays(39).Date.AddHours(23).AddMinutes(59));
        support.RecordRecognition(Noon.AddDays(40).Date);
        Assert.Equal(42, support.State.RecognitionCount);
        Assert.Equal(30, support.State.ActiveDays.Length);
        Assert.Equal(DateOnly.FromDateTime(Noon.AddDays(40)), support.State.ActiveDays[^1]);
    }

    [Fact]
    public void Debug_reset_preserves_usage_and_cannot_prompt_in_the_same_process()
    {
        var settings = WithDays(7);
        using var support = Create(settings);
        support.ConfirmSupport();
#if DEBUG
        Assert.True(support.ResetForDebugging());
        Assert.False(support.State.HasTipped);
        Assert.False(support.State.HasSupported);
        Assert.Equal(7, support.State.RecognitionCount);
        Assert.Equal(7, support.State.ActiveDays.Length);
        Assert.False(support.TryMarkStartupPrompt(Noon.AddDays(8), true));
        using var restarted = Create(settings);
        Assert.True(restarted.TryMarkStartupPrompt(Noon.AddDays(8), true));
#else
        Assert.Null(typeof(SupportService).GetMethod("ResetForDebugging"));
        Assert.True(support.State.HasTipped);
#endif
    }

    private static MemorySettings WithDays(int count)
    {
        var settings = new MemorySettings();
        using var support = Create(settings);
        for (var day = 0; day < count; day++) support.RecordRecognition(Noon.AddDays(day));
        return settings;
    }

    private static SupportService Create(MemorySettings settings) => new(settings, new ResourceManagerStringLocalizer(), new SilentLogger());

    private sealed class MemorySettings : SettingsStub
    {
        public string? Raw { get; set; }
        public bool FailWrites { get; set; }
        public override string? GetValue(string key) => key == "SupportState" ? Raw : null;
        public override void SetValue(string key, string? value)
        {
            Assert.Equal("SupportState", key);
            if (FailWrites) throw new IOException("Storage unavailable.");
            Raw = value;
        }
    }

    private sealed class SilentLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    }
}
