using System.Globalization;
using System.IO;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Logging;
using Microsoft.Data.Sqlite;

namespace KotobaSenpai.Platform.Windows.Dictionary;

public sealed class PitchAccentSqliteRepository(string databasePath, ILogger? logger = null) : IPitchAccentDictionary
{
    private const int KeysPerChunk = 400;
    private int _unavailableLogged;

    public IReadOnlyDictionary<(string Surface, string Reading), IReadOnlyList<int>> FindByKeys(
        IReadOnlyCollection<(string Surface, string Reading)> keys)
    {
        var matches = new Dictionary<(string Surface, string Reading), IReadOnlyList<int>>();
        if (keys.Count == 0)
            return matches;
        if (!File.Exists(databasePath))
        {
            Warn("Pitch accent index is missing at {0}.");
            return matches;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            foreach (var chunk in keys.Distinct().Chunk(KeysPerChunk))
            {
                using var command = connection.CreateCommand();
                var rows = new string[chunk.Length];
                for (var index = 0; index < chunk.Length; index++)
                {
                    var surface = $"$s{index}";
                    var reading = $"$r{index}";
                    rows[index] = $"({surface}, {reading})";
                    command.Parameters.AddWithValue(surface, chunk[index].Surface);
                    command.Parameters.AddWithValue(reading, chunk[index].Reading);
                }
                command.CommandText = $"""
                    WITH requested(surface, reading) AS (VALUES {string.Join(", ", rows)})
                    SELECT e.surface, e.reading, e.positions
                    FROM requested r JOIN entries e ON e.surface = r.surface AND e.reading = r.reading
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    var moraCount = PitchAccent.SplitMorae(key.Item2).Count;
                    var positions = reader.GetString(2).Split(',')
                        .Select(raw => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var position) ? position : -1)
                        .Where(position => position >= 0 && position <= moraCount)
                        .Distinct().ToArray();
                    if (positions.Length > 0)
                        matches.Add(key, positions);
                }
            }
        }
        catch (SqliteException exception)
        {
            Warn("Pitch accent index query failed at {0}.", exception);
            matches.Clear();
        }
        return matches;
    }

    private void Warn(string message, Exception? exception = null)
    {
        if (logger is not null && Interlocked.Exchange(ref _unavailableLogged, 1) == 0)
            logger.Log(LogLevel.Warning, exception, message, databasePath);
    }
}
