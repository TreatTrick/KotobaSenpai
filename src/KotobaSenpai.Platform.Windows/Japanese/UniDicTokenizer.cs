using System.IO;
using System.Text.Json;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Platform.Windows.Ocr;
using NMeCab;
using NMeCab.Specialized;

namespace KotobaSenpai.Platform.Windows.Japanese;

/// <summary>
/// Japanese tokenizer loading the UniDic unidic22 dictionary via LibNMeCab's <see cref="MeCabUniDic22Tagger"/>. The
/// dictionary directory is overridden with the <c>KOTOBA_UNIDIC_DIR</c> environment variable (development/testing);
/// otherwise it falls back to <c>%LocalAppData%/KotobaSenpai/UniDic/dicdir</c> (where the first run downloads it). A
/// directory can be injected at compile time to support offline tests (avoiding parallel tests mutating a
/// process-level environment variable). Throws <see cref="WindowsPlatformException"/>
/// (<see cref="ErrorCodes.UniDicDictionaryMissing"/>) when the dictionary is missing; throws
/// <see cref="ErrorCodes.UniDicDictionaryInvalid"/> when present but with an invalid version/format/manifest.
/// </summary>
public sealed class UniDicTokenizer : ITokenizer, IDisposable
{
    private readonly ILogger _logger;
    private readonly string _dictionaryDirectory;
    private readonly bool _requireInstalledManifest;
    private readonly Lazy<MeCabUniDic22Tagger> _tagger;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// When <paramref name="dictionaryDirectory"/> is empty, resolves via the <c>KOTOBA_UNIDIC_DIR</c> environment variable
    /// → default cache directory. Only the default cache directory requires an installed manifest (written by the
    /// installer); env-var/injected directories may lack the project manifest but must pass version/format validation.
    /// </summary>
    public UniDicTokenizer(ILogger logger, string? dictionaryDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dictionaryDirectory = dictionaryDirectory ?? ResolveDictionaryDirectory();
        _requireInstalledManifest = dictionaryDirectory is null
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOTOBA_UNIDIC_DIR"));
        _tagger = new Lazy<MeCabUniDic22Tagger>(CreateTagger, isThreadSafe: true);
    }

    /// <inheritdoc />
    public IReadOnlyList<Token> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<Token>();

        // Parse and node projection run under the same sync guard, so concurrent calls on the singleton adapter don't pollute each other's results.
        lock (_gate)
        {
            try
            {
                var nodes = _tagger.Value.Parse(text).ToArray();
                var result = new List<Token>(nodes.Length);
                foreach (var node in nodes)
                {
                    if (node.Stat == MeCabNodeStat.Bos || node.Stat == MeCabNodeStat.Eos)
                        continue;
                    if (string.IsNullOrEmpty(node.Surface))
                        continue;

                    result.Add(new Token(
                        node.Surface,
                        node.Lemma,
                        node.OrthBase,
                        node.Kana,
                        node.KanaBase,
                        node.Pron,
                        new PartsOfSpeech(node.Pos1, node.Pos2, node.Pos3, node.Pos4),
                        node.CType,
                        node.CForm,
                        node.AType,
                        /* UTF-16 code-unit start: preserves leading spaces/newlines before the word, instead of summing prior surface lengths. */
                        node.BPos + (node.RLength - node.Length)));
                }
                return result;
            }
            catch (WindowsPlatformException)
            {
                throw; // Dictionary missing/invalid: rethrow per the existing error contract; the upper layer surfaces UniDicDictionaryMissing/Invalid.
            }
            catch (Exception ex)
            {
                // OCR text is untrusted input and may contain characters native MeCab parsing cannot handle (lone surrogates/control
                // chars, etc.), causing ParseToLattice to go out of bounds. Defensive: skip the line, don't let one bad line
                // tank the whole recognition pass.
                _logger.Log(LogLevel.Warning, ex, "UniDicTokenizer: MeCab failed to parse '{text}' (skipping line)", Truncate(text));
                return Array.Empty<Token>();
            }
        }
    }

    /// <summary>Truncates log text so a whole block of OCR garbage isn't written to the log.</summary>
    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "…";

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_tagger.IsValueCreated)
            _tagger.Value.Dispose();
        _disposed = true;
    }

    /// <summary>Lazy loading throws on failure; Lazy does not cache a half-initialized instance (re-runs on next access).</summary>
    private MeCabUniDic22Tagger CreateTagger()
    {
        _logger.LogInformation("UniDicTokenizer: loading dictionary from '{dir}'", _dictionaryDirectory);
        ValidateDictionary();
        try
        {
            var tagger = MeCabUniDic22Tagger.Create(_dictionaryDirectory);
            _logger.LogInformation("UniDicTokenizer: dictionary loaded from '{dir}'", _dictionaryDirectory);
            return tagger;
        }
        catch (WindowsPlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                $"Failed to load UniDic dictionary at '{_dictionaryDirectory}'.",
                ex);
        }
    }

    private void ValidateDictionary()
    {
        var missing = UniDicAssets.RequiredRuntimeFiles
            .Where(f => !File.Exists(Path.Combine(_dictionaryDirectory, f)))
            .ToArray();
        if (missing.Length > 0)
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryMissing,
                $"UniDic dictionary missing required runtime files at '{_dictionaryDirectory}': {string.Join(", ", missing)}");

        // The archive has no version file (see UniDicDictionaryInstaller); the version is pinned by SHA-256, so only the dicrc format is checked here.
        var dicrcOk = File.Exists(Path.Combine(_dictionaryDirectory, UniDicAssets.DicrcFileName))
            && File.ReadAllText(Path.Combine(_dictionaryDirectory, UniDicAssets.DicrcFileName))
                .Contains(UniDicAssets.Format, StringComparison.Ordinal);
        if (!dicrcOk)
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                $"UniDic dictionary format mismatch at '{_dictionaryDirectory}'.");

        if (_requireInstalledManifest)
        {
            var manifestPath = Path.Combine(_dictionaryDirectory, UniDicAssets.ManifestFileName);
            if (!File.Exists(manifestPath) || !ManifestMatches(manifestPath))
                throw new WindowsPlatformException(
                    ErrorCodes.UniDicDictionaryInvalid,
                    $"UniDic dictionary installed manifest missing or invalid at '{_dictionaryDirectory}'.");
        }
    }

    private static bool ManifestMatches(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UniDicManifest>(File.ReadAllText(manifestPath));
            return manifest is not null
                && manifest.Version == UniDicAssets.Version
                && manifest.Sha256 == UniDicAssets.Sha256
                && manifest.Format == UniDicAssets.Format;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string ResolveDictionaryDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KOTOBA_UNIDIC_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.GetFullPath(overrideDir);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "UniDic", "dicdir");
    }
}