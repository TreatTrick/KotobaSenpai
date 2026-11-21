using System.IO;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Platform.Windows.Dictionary;
using KotobaSenpai.Tools.PitchAccent;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PitchAccentSqliteRepositoryTests
{
    [Fact]
    public void BatchesExactKeysAcrossChunksAndReleasesTheFile()
    {
        var path = NewPath();
        try
        {
            PitchAccentIndex.Build(path, ["頭\tこうべ\t0,3", "頭\tあたま\t3,2", "額\tひたい\t0"], "fixture");
            var keys = Enumerable.Range(0, 451).Select(i => ($"Missing{i}", "ない"))
                .Prepend(("頭", "こうべ")).Append(("額", "ひたい")).Append(("頭", "かしら"))
                .Append(("頭", "こうべ")).ToArray();
            var results = new PitchAccentSqliteRepository(path).FindByKeys(keys);
            Assert.Equal(2, results.Count);
            Assert.Equal([0, 3], results[("頭", "こうべ")]);
            Assert.Equal([0], results[("額", "ひたい")]);
            File.Delete(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableIndexLogsOneWarningAndReturnsNoMatches(bool corrupt)
    {
        var path = NewPath();
        try
        {
            if (corrupt)
                File.WriteAllText(path, "Not a database");
            var logger = new RecordingLogger();
            var repository = new PitchAccentSqliteRepository(path, logger);
            Assert.Empty(repository.FindByKeys([("額", "ひたい")]));
            Assert.Empty(repository.FindByKeys([("頭", "こうべ")]));
            Assert.Contains(path, Assert.Single(logger.Warnings));
            if (!corrupt)
                Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"pitch-{Guid.NewGuid():N}.db");

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (level == LogLevel.Warning)
                Warnings.Add(string.Format(message, args));
        }
    }
}
