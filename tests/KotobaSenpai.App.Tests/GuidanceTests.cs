using KotobaSenpai.App.Guidance;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.TestSupport;

namespace KotobaSenpai.App.Tests;

public sealed class GuidanceTests
{
    [Fact]
    public void Main_steps_only_save_on_the_last_acknowledgement_and_remain_complete_after_restart()
    {
        var settings = new MemorySettings();
        settings.Values["Theme"] = "Dark";
        var guidance = new ContextualGuidance(settings, new TestLogger());
        guidance.Main.Start();
        foreach (var target in new[] { GuidanceTarget.SelectWindow, GuidanceTarget.Recognize,
            GuidanceTarget.RecognitionRegion, GuidanceTarget.AutoRecognition })
        {
            Assert.Equal(target, guidance.Main.Current?.Target);
            Assert.Null(settings.GetValue(ContextualGuidance.MainCompletionKey));
            guidance.Main.Advance();
        }
        Assert.False(guidance.IsActive);
        Assert.Equal(ContextualGuidance.CompletedValue, settings.GetValue(ContextualGuidance.MainCompletionKey));
        Assert.Equal("Dark", settings.GetValue("Theme"));
        var restarted = new ContextualGuidance(settings, new TestLogger());
        restarted.Main.Start();
        Assert.Null(restarted.Main.Current);
        restarted.Region.Start();
        Assert.Equal(GuidanceTarget.RegionTopLeft, restarted.Region.Current?.Target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("TRUE")]
    public void Interrupted_tours_restart_without_saving_and_duplicate_start_preserves_the_current_step(string? saved)
    {
        var settings = new MemorySettings();
        settings.Values[ContextualGuidance.RegionCompletionKey] = saved;
        var tour = new ContextualGuidance(settings, new TestLogger()).Region;
        tour.Start();
        tour.Advance();
        tour.Start();
        Assert.Equal(GuidanceTarget.RegionConfirm, tour.Current?.Target);
        tour.Stop();
        Assert.Equal(saved, settings.GetValue(ContextualGuidance.RegionCompletionKey));
        tour.Advance();
        tour.Start();
        Assert.Equal(GuidanceTarget.RegionTopLeft, tour.Current?.Target);
    }

    [Fact]
    public void Failed_completion_ends_the_session_without_suppressing_the_next_process()
    {
        var settings = new MemorySettings { FailWrites = true };
        var logger = new TestLogger();
        var guidance = new ContextualGuidance(settings, logger);
        guidance.Region.Start();
        guidance.Region.Advance();
        guidance.Region.Advance();
        guidance.Region.Start();
        Assert.False(guidance.IsActive);
        Assert.Single(logger.Errors);
        Assert.Null(settings.GetValue(ContextualGuidance.RegionCompletionKey));
        var restarted = new ContextualGuidance(settings, logger);
        restarted.Region.Start();
        Assert.NotNull(restarted.Region.Current);
    }

    internal sealed class MemorySettings : SettingsStub
    {
        public Dictionary<string, string?> Values { get; } = [];
        public bool FailWrites { get; init; }
        public override string? GetValue(string key) => Values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value)
        {
            if (FailWrites) throw new InvalidOperationException("Settings are unavailable.");
            Values[key] = value;
        }
    }

    internal sealed class TestLogger : ILogger
    {
        public List<Exception> Errors { get; } = [];
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (exception is not null) Errors.Add(exception);
        }
    }
}
