using System.IO;
using KotobaSenpai.Platform.Windows.Dictionary;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public void Missing_db_returns_empty_without_throwing()
    {
        var repo = new JmdictSqliteRepository(
            Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".db"));
        Assert.Empty(repo.FindByKanji("受ける"));
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

    /// <summary>构造与 JmdictIndexBuilder 相同 schema 的临时库（entries/kanji/reading + 索引）。</summary>
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

    private static void AddDuplicateReadingIndex(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO reading VALUES ('受ける', 1)";
        cmd.ExecuteNonQuery();
    }
}
