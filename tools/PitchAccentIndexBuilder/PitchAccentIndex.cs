using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KotobaSenpai.Core.Japanese;
using Microsoft.Data.Sqlite;

namespace KotobaSenpai.Tools.PitchAccent;

public static partial class PitchAccentIndex
{
    [GeneratedRegex(@"^(?:\([^)]*\)|（[^）]*）)\s*")]
    private static partial Regex PartOfSpeechPrefix();

    private static bool IsReadingCharacter(char character)
        => character is >= 'ぁ' and <= 'ゖ' or 'ゝ' or 'ゞ' or 'ー';

    public static int Build(string databasePath, IEnumerable<string> lines, string sourceVersion)
    {
        var entries = new Dictionary<(string Surface, string Reading), List<int>>();
        foreach (var line in lines)
        {
            var columns = line.Split('\t');
            if (columns.Length != 3)
                continue;

            var surface = columns[0].Trim().TrimStart('\uFEFF');
            var rawReading = columns[1].Trim();
            var hiragana = Kana.ToHiragana((rawReading.Length == 0 ? surface : rawReading).Normalize(NormalizationForm.FormKC));
            if (surface.Length == 0 || (rawReading.Length == 0 && !hiragana.All(IsReadingCharacter)))
                continue;
            var reading = new string(hiragana.Where(IsReadingCharacter).ToArray());
            var moraCount = Core.Japanese.PitchAccent.SplitMorae(reading).Count;
            if (moraCount == 0)
                continue;

            foreach (var rawPosition in columns[2].Split(','))
            {
                var number = PartOfSpeechPrefix().Replace(rawPosition.Trim(), string.Empty);
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
                    || position > moraCount)
                    continue;

                var key = (surface, reading);
                if (!entries.TryGetValue(key, out var positions))
                    entries.Add(key, positions = []);
                if (!positions.Contains(position))
                    positions.Add(position);
            }
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The pitch accent source contains no valid entries.");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP TABLE IF EXISTS meta;
            DROP TABLE IF EXISTS entries;
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE entries (
                surface TEXT NOT NULL,
                reading TEXT NOT NULL,
                positions TEXT NOT NULL,
                PRIMARY KEY (surface, reading)
            ) WITHOUT ROWID;
            INSERT INTO meta VALUES ('schema_version', '1'), ('source', 'Kanjium accents.txt'),
                ('source_version', $version), ('license', 'CC BY-SA 4.0');
            """;
        command.Parameters.AddWithValue("$version", sourceVersion);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO entries VALUES ($surface, $reading, $positions)";
        var surfaceParameter = command.Parameters.Add("$surface", SqliteType.Text);
        var readingParameter = command.Parameters.Add("$reading", SqliteType.Text);
        var positionsParameter = command.Parameters.Add("$positions", SqliteType.Text);
        command.Prepare();
        foreach (var (key, positions) in entries)
        {
            surfaceParameter.Value = key.Surface;
            readingParameter.Value = key.Reading;
            positionsParameter.Value = string.Join(',', positions.Select(position => position.ToString(CultureInfo.InvariantCulture)));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return entries.Count;
    }
}
