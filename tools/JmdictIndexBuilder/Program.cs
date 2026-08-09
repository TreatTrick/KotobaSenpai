using System.Text.Json;
using KotobaSenpai.Core.Models;
using Microsoft.Data.Sqlite;

// 构建工具：jmdict-simplified JSON → KotobaSenpai 用的 SQLite 词典索引。
// 用法: JmdictIndexBuilder [jsonPath] [dbOut]
//   jsonPath 省略时从 jmdict-simplified 最新 release 下载 JMdict_e.json。
//   dbOut 默认 ./jmdict.db。
// 输出 schema 与 JmdictSqliteRepository 一致：entries / kanji / reading 三表。

var jsonPath = args.Length > 0 ? args[0] : null;
var dbOut = args.Length > 1 ? args[1] : "jmdict.db";

if (jsonPath is null)
    jsonPath = await DownloadLatestEnglishJsonAsync();

using var doc = JsonDocument.Parse(File.OpenRead(jsonPath));
var root = doc.RootElement;
var words = root.TryGetProperty("words", out var w) ? w : root;

if (File.Exists(dbOut))
    File.Delete(dbOut);

using var conn = new SqliteConnection($"Data Source={dbOut}");
conn.Open();
CreateSchema(conn);

using var tx = conn.BeginTransaction();
var entryCmd = conn.CreateCommand();
entryCmd.CommandText = "INSERT INTO entries(id, headword, reading, senses) VALUES (@id, @headword, @reading, @senses)";
var kanjiCmd = conn.CreateCommand();
kanjiCmd.CommandText = "INSERT INTO kanji(form, entry_id) VALUES (@form, @id)";
var readingCmd = conn.CreateCommand();
readingCmd.CommandText = "INSERT INTO reading(form, entry_id) VALUES (@form, @id)";

int id = 0;
foreach (var word in words.EnumerateArray())
{
    var kanjiList = TextList(word, "kanji");
    var kanaList = TextList(word, "kana");
    var senses = ReadSenses(word);
    if ((kanjiList.Count == 0 && kanaList.Count == 0) || senses.Count == 0)
        continue;

    id++;
    var headword = kanjiList.FirstOrDefault() ?? kanaList[0];
    var reading = kanaList.FirstOrDefault() ?? "";

    entryCmd.Parameters.Clear();
    entryCmd.Parameters.AddWithValue("@id", id);
    entryCmd.Parameters.AddWithValue("@headword", headword);
    entryCmd.Parameters.AddWithValue("@reading", reading);
    entryCmd.Parameters.AddWithValue("@senses", JsonSerializer.Serialize(senses));
    entryCmd.ExecuteNonQuery();

    foreach (var k in kanjiList)
    {
        kanjiCmd.Parameters.Clear();
        kanjiCmd.Parameters.AddWithValue("@form", k);
        kanjiCmd.Parameters.AddWithValue("@id", id);
        kanjiCmd.ExecuteNonQuery();
    }
    foreach (var k in kanaList)
    {
        readingCmd.Parameters.Clear();
        readingCmd.Parameters.AddWithValue("@form", k);
        readingCmd.Parameters.AddWithValue("@id", id);
        readingCmd.ExecuteNonQuery();
    }
}
tx.Commit();

Console.WriteLine($"Built {dbOut}: {id} entries.");

static List<string> TextList(JsonElement word, string prop)
{
    if (!word.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
        return [];
    var list = new List<string>();
    foreach (var item in arr.EnumerateArray())
        if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(t.GetString()))
            list.Add(t.GetString()!);
    return list;
}

static List<DictionarySense> ReadSenses(JsonElement word)
{
    if (!word.TryGetProperty("sense", out var senses) || senses.ValueKind != JsonValueKind.Array)
        return [];
    var result = new List<DictionarySense>();
    foreach (var s in senses.EnumerateArray())
    {
        var pos = StringList(s, "partOfSpeech");
        var glosses = GlossTexts(s);
        if (glosses.Count == 0)
            continue;
        result.Add(new DictionarySense(pos, glosses));
    }
    return result;

    static List<string> StringList(JsonElement sense, string prop)
    {
        if (!sense.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        return list;
    }

    // jmdict-simplified 的释义字段是 "gloss"（单数），每个元素是 {"lang","text",...} 对象。
    static List<string> GlossTexts(JsonElement sense)
    {
        if (!sense.TryGetProperty("gloss", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var g in arr.EnumerateArray())
            if (g.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(t.GetString()))
                list.Add(t.GetString()!);
        return list;
    }
}

static void CreateSchema(SqliteConnection conn)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE entries (
            id INTEGER PRIMARY KEY,
            headword TEXT NOT NULL,
            reading TEXT,
            senses TEXT NOT NULL
        );
        CREATE TABLE kanji (
            form TEXT NOT NULL,
            entry_id INTEGER NOT NULL
        );
        CREATE TABLE reading (
            form TEXT NOT NULL,
            entry_id INTEGER NOT NULL
        );
        CREATE INDEX idx_kanji_form ON kanji(form);
        CREATE INDEX idx_reading_form ON reading(form);
        """;
    cmd.ExecuteNonQuery();
}

static async Task<string> DownloadLatestEnglishJsonAsync()
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("KotobaSenpai-JmdictIndexBuilder");

    // 解析最新 release，取 jmdict-eng-*.json.zip（发行资产带版本号，非固定名）。
    using var release = JsonDocument.Parse(
        await client.GetStringAsync("https://api.github.com/repos/scriptin/jmdict-simplified/releases/latest"));
    string? url = null;
    foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
    {
        var name = asset.GetProperty("name").GetString();
        if (name is not null && name.StartsWith("jmdict-eng-") && name.EndsWith(".json.zip"))
        {
            url = asset.GetProperty("browser_download_url").GetString();
            break;
        }
    }
    if (url is null)
        throw new InvalidOperationException("No English JMdict asset found in latest release.");

    Console.WriteLine($"Downloading {url} ...");
    var zipPath = Path.Combine(Path.GetTempPath(), "jmdict-eng.zip");
    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
    {
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(zipPath);
        await source.CopyToAsync(target);
    }

    var extractDir = Path.Combine(Path.GetTempPath(), "jmdict-eng-" + Guid.NewGuid().ToString("N"));
    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);
    return Directory.GetFiles(extractDir, "*.json")[0];
}