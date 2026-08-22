using System.IO;
using KotobaSenpai.App.Diagnostics;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class FileDiagnosticReporterTests
{
    [Fact]
    public void Uses_one_recognition_id_for_all_diagnostic_file_names()
    {
        var dir = NewTempDir();
        var recognitionId = Guid.Parse("0123456789abcdef0123456789abcdef");
        var reporter = new FileDiagnosticReporter(new FakeSettings(true), dir);
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));

        reporter.RecordTokens(recognitionId, target, []);
        reporter.RecordPhraseAnalysis(recognitionId, PhraseAnalysisOutcome.Success, [], null);
        reporter.RecordLlmExchange(recognitionId, "segment:with-invalid", "{}", "{}");

        var names = Directory.GetFiles(dir).Select(Path.GetFileName).OfType<string>().ToArray();
        Assert.Contains($"tokens-{recognitionId:N}.txt", names);
        Assert.Contains($"phrase-{recognitionId:N}.txt", names);

        var request = Assert.Single(names, name => name.StartsWith("llm-req-", StringComparison.Ordinal));
        var response = Assert.Single(names, name => name.StartsWith("llm-resp-", StringComparison.Ordinal));
        Assert.Contains(recognitionId.ToString("N"), request);
        Assert.Contains("segment_with-invalid", request);
        Assert.Equal(request.Replace("llm-req-", "llm-resp-", StringComparison.Ordinal), response);
    }

    [Fact]
    public void Does_not_write_files_when_diagnostics_are_disabled()
    {
        var dir = NewTempDir();
        var reporter = new FileDiagnosticReporter(new FakeSettings(false), dir);
        var id = Guid.NewGuid();
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));

        reporter.RecordTokens(id, target, []);
        reporter.RecordPhraseAnalysis(id, PhraseAnalysisOutcome.Success, [], null);
        reporter.RecordLlmExchange(id, "s0", "{}", "{}");

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Retains_only_ten_token_files_per_prefix()
    {
        var dir = NewTempDir();
        var reporter = new FileDiagnosticReporter(new FakeSettings(true), dir);
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));

        for (var i = 0; i < 11; i++)
            reporter.RecordTokens(Guid.NewGuid(), target, []);

        Assert.Equal(10, Directory.GetFiles(dir, "tokens-*").Length);
    }

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "kotoba-diag-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeSettings : ISettingsService
    {
        private readonly bool _enabled;

        public FakeSettings(bool enabled) => _enabled = enabled;

        public string? GetValue(string key)
            => key == "DiagEnabled" && _enabled ? "true" : null;

        public void SetValue(string key, string? value) { }
    }
}
