using System.IO;
using Microsoft.Data.Sqlite;
using KotobaSenpai.Tools.PitchAccent;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PitchAccentIndexBuilderTests
{
    [Fact]
    public void RebuildReplacesEntriesAndSourceVersionAtTheSamePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pitch-{Guid.NewGuid():N}.db");
        try
        {
            PitchAccentIndex.Build(path, ["頭\tあたま\t3"], "original");
            Assert.Equal(1, PitchAccentIndex.Build(path, ["額\tひたい\t0"], "updated"));
            Assert.Throws<InvalidDataException>(() => PitchAccentIndex.Build(path, ["invalid"], "invalid"));

            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT surface FROM entries";
            Assert.Equal("額", command.ExecuteScalar());
            command.CommandText = "SELECT COUNT(*) FROM entries";
            Assert.Equal(1L, command.ExecuteScalar());
            command.CommandText = "SELECT value FROM meta WHERE key = 'source_version'";
            Assert.Equal("updated", command.ExecuteScalar());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildsExactKeysAndKeepsOrderedValidPositions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pitch-{Guid.NewGuid():N}.db");
        try
        {
            PitchAccentIndex.Build(path,
                ["一週間\tイッシューカン\t3", "あした\t\t3,0", "頭\tこう・べ\t(名)0,4,3,0,-1",
                    "頭\tこうべ\t（副）2,3", "頭\tあたま\t3", "頭\t\t1"], "fixture");
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT positions FROM entries WHERE surface = $surface AND reading = $reading";
            var surface = command.Parameters.AddWithValue("$surface", "頭");
            var reading = command.Parameters.AddWithValue("$reading", "こうべ");
            Assert.Equal("0,3,2", command.ExecuteScalar());
            reading.Value = "あたま";
            Assert.Equal("3", command.ExecuteScalar());
            reading.Value = "かしら";
            Assert.Null(command.ExecuteScalar());
            surface.Value = reading.Value = "あした";
            Assert.Equal("3,0", command.ExecuteScalar());
            surface.Value = "一週間";
            reading.Value = "いっしゅーかん";
            Assert.Equal("3", command.ExecuteScalar());
            command.CommandText = "SELECT value FROM meta WHERE key = 'source_version'";
            Assert.Equal("fixture", command.ExecuteScalar());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
