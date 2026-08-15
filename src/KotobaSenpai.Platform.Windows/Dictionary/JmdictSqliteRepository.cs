using System.IO;
using System.Text.Json;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using Microsoft.Data.Sqlite;

namespace KotobaSenpai.Platform.Windows.Dictionary;

/// <summary>
/// SQLite implementation of JMdict: opens the bundled .db and queries entries by written form (kanji table) or hiragana
/// reading (reading table). Each lookup opens a connection on demand (low-frequency, single items); data stays on disk
/// and is not kept in memory. Missing data or a failed query returns empty rather than crashing.
/// </summary>
public sealed class JmdictSqliteRepository : IJmdictRepository
{
    private const int BatchParameterChunkSize = 400;
    private readonly string _dbPath;

    public JmdictSqliteRepository(string dbPath)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
    }

    public IReadOnlyList<DictionaryEntry> FindByKanji(string kanji) => Query("kanji", kanji);

    public IReadOnlyList<DictionaryEntry> FindByKana(string kana) => Query("reading", kana);

    public IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> FindByForms(
        IReadOnlyCollection<string> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);
        var requested = forms
            .Where(form => !string.IsNullOrEmpty(form))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0 || !File.Exists(_dbPath))
            return new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal);

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            connection.Open();
            var buckets = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);

            foreach (var chunk in requested.Chunk(BatchParameterChunkSize))
            {
                using var command = connection.CreateCommand();
                var parameters = new List<string>(chunk.Length);
                for (int i = 0; i < chunk.Length; i++)
                {
                    var name = $"@p{i}";
                    parameters.Add(name);
                    command.Parameters.AddWithValue(name, chunk[i]);
                }

                var inClause = string.Join(", ", parameters);
                command.CommandText = $"""
                    SELECT f.form, e.headword, e.reading, e.senses
                    FROM (
                        SELECT form, entry_id FROM kanji WHERE form IN ({inClause})
                        UNION
                        SELECT form, entry_id FROM reading WHERE form IN ({inClause})
                    ) f
                    JOIN entries e ON e.id = f.entry_id
                    """;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var form = reader.GetString(0);
                    var entry = new DictionaryEntry(
                        reader.GetString(1),
                        reader.GetString(2),
                        JsonSerializer.Deserialize<DictionarySense[]>(reader.GetString(3)) ?? []);
                    if (!buckets.TryGetValue(form, out var entries))
                    {
                        entries = [];
                        buckets[form] = entries;
                    }
                    entries.Add(entry);
                }
            }

            return buckets.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<DictionaryEntry>)pair.Value.ToArray(),
                StringComparer.Ordinal);
        }
        catch (SqliteException)
        {
            return new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal);
        }
    }

    private IReadOnlyList<DictionaryEntry> Query(string table, string form)
    {
        if (string.IsNullOrEmpty(form) || !File.Exists(_dbPath))
            return Array.Empty<DictionaryEntry>();

        try
        {
            // Pooling=False: open/close per query, avoiding long-held handles on the .db file.
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            // table comes only from the internal constants "kanji"/"reading", not user input.
            command.CommandText = $"""
                SELECT e.headword, e.reading, e.senses
                FROM entries e
                JOIN {table} t ON t.entry_id = e.id
                WHERE t.form = @form
                """;
            command.Parameters.AddWithValue("@form", form);

            using var reader = command.ExecuteReader();
            var result = new List<DictionaryEntry>();
            while (reader.Read())
            {
                var senses = JsonSerializer.Deserialize<DictionarySense[]>(reader.GetString(2)) ?? [];
                result.Add(new DictionaryEntry(reader.GetString(0), reader.GetString(1), senses));
            }
            return result;
        }
        catch (SqliteException)
        {
            return Array.Empty<DictionaryEntry>();
        }
    }
}
