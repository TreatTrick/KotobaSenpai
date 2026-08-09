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
}