using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace KotobaSenpai.App.Diagnostics;

public sealed record DiagnosticBundleFile(string SourcePath, string EntryName);

public static class DiagnosticBundleExporter
{
    private static readonly (string Prefix, string Extension)[] DiagnosticTypes =
    [
        ("frame-", ".png"),
        ("ocr-", ".txt"),
        ("tokens-", ".txt"),
        ("phrase-", ".txt"),
        ("llm-req-", ".json"),
        ("llm-resp-", ".json"),
        ("llm-groups-", ".json"),
        ("llm-words-", ".json"),
    ];

    public static IReadOnlyList<DiagnosticBundleFile> Collect(string logsDirectory, string diagnosticsDirectory)
    {
        var files = new List<DiagnosticBundleFile>();
        AddLogs(files, logsDirectory);
        AddDiagnostics(files, diagnosticsDirectory);
        return files.OrderBy(file => file.EntryName, StringComparer.Ordinal).ToArray();
    }

    public static void Create(IReadOnlyList<DiagnosticBundleFile> files, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (files.Count == 0)
            throw new ArgumentException("A diagnostic bundle requires at least one file.", nameof(files));

        var destination = Path.GetFullPath(destinationPath);
        if (files.Any(file => string.Equals(Path.GetFullPath(file.SourcePath), destination, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The destination cannot replace a source diagnostic file.", nameof(destinationPath));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    var entry = archive.CreateEntry(file.EntryName, CompressionLevel.Optimal);
                    using var source = File.OpenRead(file.SourcePath);
                    using var target = entry.Open();
                    source.CopyTo(target);
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void AddLogs(List<DiagnosticBundleFile> files, string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (name.Length != 27 || !name.StartsWith("kotobasenpai-", StringComparison.Ordinal)
                || !name.EndsWith(".log", StringComparison.Ordinal)
                || !DateTime.TryParseExact(name.AsSpan(13, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)
                || date.Date < DateTime.Today.AddDays(-7) || date.Date > DateTime.Today)
                continue;

            files.Add(new DiagnosticBundleFile(path, "logs/" + name));
        }
    }

    private static void AddDiagnostics(List<DiagnosticBundleFile> files, string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (!DiagnosticTypes.Any(type => name.StartsWith(type.Prefix, StringComparison.Ordinal)
                    && name.EndsWith(type.Extension, StringComparison.OrdinalIgnoreCase)))
                continue;

            files.Add(new DiagnosticBundleFile(path, "diag/" + name));
        }
    }
}
