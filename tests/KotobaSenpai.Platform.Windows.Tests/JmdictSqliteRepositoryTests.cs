using System.IO;
using KotobaSenpai.Platform.Windows.Dictionary;
using Microsoft.Data.Sqlite;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class JmdictSqliteRepositoryTests
{
    [Fact]
    public void Finds_entry_by_kanji_and_reading()
    {
        var db = CreateDb();
        try
        {
            var repo = new JmdictSqliteRepository(db);

            var byKanji = repo.FindByKanji("受ける");
            var byKana = repo.FindByKana("うける");

            var entry = Assert.Single(byKanji);
            Assert.Single(byKana);
            Assert.Equal("受ける", entry.Headword);
            Assert.Equal("to receive", entry.Senses[0].Glosses[0]);
        }
        finally
        {
            File.Delete(db);
        }
    }

    [Fact]
    public void Missing_form_returns_empty()
    {
        var db = CreateDb();
        try
        {
            var repo = new JmdictSqliteRepository(db);
            Assert.Empty(repo.FindByKanji("存在しない"));
            Assert.Empty(repo.FindByKana("ぞんざい"));
        }
        finally
        {
            File.Delete(db);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_db_returns_empty_and_logs_once(bool batchFirst)
    {
        var path = Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".db");
        var logger = new RecordingLogger();
        var repo = new JmdictSqliteRepository(path, logger);

        if (batchFirst)
            Assert.Empty(repo.FindByForms(["受ける"]));
        Assert.Empty(repo.FindByKanji("受ける"));
        Assert.Empty(repo.FindByKana("うける"));
        Assert.Empty(repo.FindByForms(["受ける"]));

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("JMdict index is missing", warning);
        Assert.Contains(path, warning);
    }

    [Fact]
    public void Finds_multiple_forms_in_one_batch_and_deduplicates_indexes()
    {
        var db = CreateDb();
        try
        {
            AddDuplicateReadingIndex(db);
            var repo = new JmdictSqliteRepository(db);

            var result = repo.FindByForms(["受ける", "うける", "存在しない"]);

            Assert.Equal(2, result.Count);
            Assert.Single(result["受ける"]);
            Assert.Single(result["うける"]);
            Assert.Equal("受ける", result["うける"][0].Headword);
            Assert.DoesNotContain("存在しない", result.Keys);
        }
        finally
        {
            File.Delete(db);
        }
    }

    [Fact]
    public void Batch_query_chunks_large_form_sets()
    {
        var db = CreateDb();
        try
        {
            var forms = Enumerable.Range(0, 405)
                .Select(i => $"不存在{i}")
                .Append("受ける")
                .ToArray();
            var repo = new JmdictSqliteRepository(db);

            var result = repo.FindByForms(forms);

            Assert.Single(result);
            Assert.Single(result["受ける"]);
        }
        finally
        {
            File.Delete(db);
        }
    }

    /// <summary>Builds a temp database with the same schema as JmdictIndexBuilder (entries/kanji/reading + indexes).</summary>
    private static string CreateDb()
    {
        var path = Path.Combine(Path.GetTempPath(), "jmdict-test-" + Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE entries (id INTEGER PRIMARY KEY, headword TEXT NOT NULL, reading TEXT, senses TEXT NOT NULL);
            CREATE TABLE kanji (form TEXT NOT NULL, entry_id INTEGER NOT NULL);
            CREATE TABLE reading (form TEXT NOT NULL, entry_id INTEGER NOT NULL);
            CREATE INDEX idx_kanji_form ON kanji(form);
            CREATE INDEX idx_reading_form ON reading(form);
            INSERT INTO entries VALUES (1, '受ける', 'うける', '[{"Pos":["動詞"],"Glosses":["to receive"]}]');
            INSERT INTO kanji VALUES ('受ける', 1);
            INSERT INTO reading VALUES ('うける', 1);
            """;
        cmd.ExecuteNonQuery();
        return path;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (level == LogLevel.Warning)
                Warnings.Add(string.Format(message, args));
        }
    }

    private static void AddDuplicateReadingIndex(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO reading VALUES ('受ける', 1)";
        cmd.ExecuteNonQuery();
    }
}
