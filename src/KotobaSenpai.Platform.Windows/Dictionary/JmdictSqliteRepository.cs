using System.IO;
using System.Text.Json;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using Microsoft.Data.Sqlite;

namespace KotobaSenpai.Platform.Windows.Dictionary;

/// <summary>
/// JMdict 的 SQLite 实现：打开捆绑 .db，按表记（kanji 表）或平假读音（reading 表）查询条目。
/// 每次查询按需打开连接（低频、单条），数据待磁盘不常驻内存；缺失或查询失败返回空不崩溃。
/// </summary>
public sealed class JmdictSqliteRepository : IJmdictRepository
{
    private readonly string _dbPath;

    public JmdictSqliteRepository(string dbPath)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
    }

    public IReadOnlyList<DictionaryEntry> FindByKanji(string kanji) => Query("kanji", kanji);

    public IReadOnlyList<DictionaryEntry> FindByKana(string kana) => Query("reading", kana);

    private IReadOnlyList<DictionaryEntry> Query(string table, string form)
    {
        if (string.IsNullOrEmpty(form) || !File.Exists(_dbPath))
            return Array.Empty<DictionaryEntry>();

        try
        {
            // Pooling=False：每次查询开/关，避免长期持有 .db 文件句柄。
            using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            // table 仅来自内部常量 "kanji"/"reading"，非用户输入。
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