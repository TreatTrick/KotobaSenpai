using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace KotobaSenpai.Architecture.Tests;

public sealed class CoreSliceArchitectureTests
{
    private static readonly Assembly CoreAssembly = typeof(KotobaSenpai.Core.Workflow.WordOverlayApplicationService).Assembly;

    public static IEnumerable<object[]> SliceLayers() =>
    [
        ["Geometry", 0],
        ["Japanese", 0],
        ["Errors", 0],
        ["Localization", 0],
        ["Logging", 0],
        ["Settings", 0],
        ["TargetWindow", 1],
        ["Ocr", 2],
        ["Tokenization", 3],
        ["Dictionary", 4],
        ["WordGrouping", 5],
        ["PhraseAnalysis", 5],
        ["Diagnostics", 6],
        ["Overlay", 6],
        ["Workflow", 7],
    ];

    [Theory]
    [MemberData(nameof(SliceLayers))]
    public void Core_slice_must_depend_only_on_lower_layers(string slice, int layer)
    {
        var forbidden = SliceLayers()
            .Select(row => (Slice: (string)row[0], Layer: (int)row[1]))
            .Where(row => row.Slice != slice && row.Layer >= layer)
            .Select(row => $"KotobaSenpai.Core.{row.Slice}")
            .ToArray();

        if (forbidden.Length == 0)
            return;

        var result = Types.InAssembly(CoreAssembly)
            .That().ResideInNamespaceMatching($"KotobaSenpai\\.Core\\.{slice}(\\..*)?$")
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Core slice {slice} (layer {layer}) must not depend on the same or a higher layer. "
            + $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Core_must_not_use_technical_or_bucket_namespaces()
    {
        var forbiddenSegments = new HashSet<string>(StringComparer.Ordinal)
        {
            "Models",
            "Services",
            "Contracts",
            "Shared",
            "Common",
            "Util",
            "Utils",
            "Helpers",
            "Misc",
            "Infrastructure",
        };
        var offenders = CoreAssembly.GetTypes()
            .Where(type => type.Namespace?.Split('.').Any(forbiddenSegments.Contains) == true)
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .Concat(FindNamespaceFolderOffenders())
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Core contains a forbidden namespace or a namespace/folder mismatch: {string.Join(", ", offenders)}");
    }

    private static IEnumerable<string> FindNamespaceFolderOffenders()
    {
        var root = FindRepositoryRoot();
        var coreRoot = Path.Combine(root, "src", "KotobaSenpai.Core");
        return Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.OrdinalIgnoreCase))
            .Select(path =>
            {
                var relative = Path.GetRelativePath(coreRoot, path);
                var directory = Path.GetDirectoryName(relative)?.Replace(Path.DirectorySeparatorChar, '.') ?? string.Empty;
                var expected = string.IsNullOrEmpty(directory)
                    ? "KotobaSenpai.Core"
                    : $"KotobaSenpai.Core.{directory}";
                var source = File.ReadAllText(path);
                var actual = Regex.Match(source, @"namespace\s+([A-Za-z0-9_.]+)\s*;").Groups[1].Value;
                return (Path: Path.GetRelativePath(root, path), Expected: expected, Actual: actual);
            })
            .Where(item => !string.Equals(item.Expected, item.Actual, StringComparison.Ordinal))
            .Select(item => $"{item.Path}: expected {item.Expected}, found {item.Actual}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;

        throw new InvalidOperationException("Repository root could not be located from the test output directory.");
    }
}
