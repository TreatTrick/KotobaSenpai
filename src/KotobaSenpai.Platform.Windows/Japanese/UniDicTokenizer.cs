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
/// 用 LibNMeCab 的 <see cref="MeCabUniDic22Tagger"/> 加载 UniDic unidic22 词典的日语分词器。
/// 词典目录用 <c>KOTOBA_UNIDIC_DIR</c> 环境变量覆盖（开发/测试），否则回退到
/// <c>%LocalAppData%/KotobaSenpai/UniDic/dicdir</c>（M1 首次运行下载的位置）。
/// 编译期可注入目录以支持无网络测试（避免并行测试改进程级环境变量）。
/// 词典缺失抛 <see cref="WindowsPlatformException"/>（<see cref="ErrorCodes.UniDicDictionaryMissing"/>）；
/// 存在但版本/格式/manifest 无效抛 <see cref="ErrorCodes.UniDicDictionaryInvalid"/>。
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
    /// <paramref name="dictionaryDirectory"/> 为空时按 环境变量 <c>KOTOBA_UNIDIC_DIR</c> → 默认缓存目录 解析。
    /// 仅默认缓存目录要求已安装 manifest（安装器写入）；环境变量/注入目录可无项目 manifest，但须过版本/格式校验。
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

        // Parse 与节点投影在同一同步保护内，保证单例适配器的并发调用结果不互相污染。
        lock (_gate)
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
                    /* UTF-16 code-unit 起点：保留词前空格/换行，不用前序表面长度累加。 */
                    node.BPos + (node.RLength - node.Length)));
            }
            return result;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_tagger.IsValueCreated)
            _tagger.Value.Dispose();
        _disposed = true;
    }

    /// <summary>懒加载失败抛异常，Lazy 不缓存半初始化实例（下次访问重跑）。</summary>
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

        // 档案不含 version 文件（见 UniDicDictionaryInstaller）；版本由 SHA-256 固定，此处仅校验 dicrc 格式。
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