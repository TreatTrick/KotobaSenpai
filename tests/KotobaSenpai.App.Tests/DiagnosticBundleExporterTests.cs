using System.IO.Compression;
using System.IO;
using KotobaSenpai.App.Diagnostics;

namespace KotobaSenpai.App.Tests;

public sealed class DiagnosticBundleExporterTests
{
    [Fact]
    public void Collects_only_recognized_log_and_diagnostic_files()
    {
        var root = CreateTemporaryDirectory();
        var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
        var diag = Directory.CreateDirectory(Path.Combine(root, "diag")).FullName;
        File.WriteAllText(Path.Combine(logs, $"kotobasenpai-{DateTime.Today:yyyy-MM-dd}.log"), "log");
        File.WriteAllText(Path.Combine(logs, "settings.json"), "settings");
        File.WriteAllText(Path.Combine(logs, "kotobasenpai-invalid.log"), "invalid");
        File.WriteAllText(Path.Combine(diag, "frame-abc.png"), "frame");
        File.WriteAllText(Path.Combine(diag, "llm-req-abc.json"), "request");
        File.WriteAllText(Path.Combine(diag, "credentials.json"), "secret");

        try
        {
            var files = DiagnosticBundleExporter.Collect(logs, diag);

            Assert.Equal(["diag/frame-abc.png", "diag/llm-req-abc.json", $"logs/kotobasenpai-{DateTime.Today:yyyy-MM-dd}.log"],
                files.Select(file => file.EntryName));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Creates_zip_with_classified_relative_paths()
    {
        var root = CreateTemporaryDirectory();
        var source = Path.Combine(root, "sample.txt");
        var destination = Path.Combine(root, "diagnostics.zip");
        File.WriteAllText(source, "diagnostic content");

        try
        {
            DiagnosticBundleExporter.Create([new DiagnosticBundleFile(source, "diag/ocr-run.txt")], destination);

            using var archive = ZipFile.OpenRead(destination);
            var entry = Assert.Single(archive.Entries);
            Assert.Equal("diag/ocr-run.txt", entry.FullName);
            using var reader = new StreamReader(entry.Open());
            Assert.Equal("diagnostic content", reader.ReadToEnd());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Keeps_existing_destination_when_source_cannot_be_read()
    {
        var root = CreateTemporaryDirectory();
        var destination = Path.Combine(root, "diagnostics.zip");
        File.WriteAllText(destination, "existing archive");

        try
        {
            Assert.Throws<FileNotFoundException>(() => DiagnosticBundleExporter.Create(
                [new DiagnosticBundleFile(Path.Combine(root, "missing.txt"), "diag/missing.txt")], destination));
            Assert.Equal("existing archive", File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ksdiag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
