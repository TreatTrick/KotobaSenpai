namespace KotobaSenpai.Core.Japanese;

/// <summary>
/// Frozen inventory of UniDic dictionary assets: the Doki-compatible <c>unidic-py</c> build
/// <c>3.1.0+2021-08-31</c>. The dictionary data is BSD-licensed (NINJAL), handled separately from LibNMeCab's
/// GPL/LGPL dual license. Used only during install/validation; the tokenizer and installer share the same
/// pinned version and format conventions through this type.
/// </summary>
public static class UniDicAssets
{
    /// <summary>Pinned version (aligned with the unidic-py build used by the Doki Windows release).</summary>
    public const string Version = "3.1.0+2021-08-31";

    /// <summary>Pinned download URL (resolving latest aliases or unverified mirrors is forbidden).</summary>
    public const string SourceUrl = "https://cotonoha-dic.s3-ap-northeast-1.amazonaws.com/unidic-3.1.0.zip";

    /// <summary>SHA-256 of the compressed dictionary package (measured 2026-08-08, downloaded from <see cref="SourceUrl"/>).</summary>
    public const string Sha256 = "638718c4c63625ab300de4c92c67925d54c0e9e3830009eaa992f29819d59c43";

    /// <summary>MeCab feature format (unified as unidic22 for UniDic 2.2+).</summary>
    public const string Format = "unidic22";

    /// <summary>The four files required at runtime by LibNMeCab 0.10.2 (other files such as model.bin are optional and their absence doesn't affect loading; they're kept as-is with the package).</summary>
    public static readonly string[] RequiredRuntimeFiles = ["char.bin", "matrix.bin", "sys.dic", "unk.dic"];

    /// <summary>The manifest file name written by the installer.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>The MeCab config bundled with the dictionary (must contain output-format-type = unidic22).</summary>
    public const string DicrcFileName = "dicrc";
}

/// <summary>Manifest content the installer writes into the install directory (version/source/hash/format/file set).</summary>
public sealed record UniDicManifest(
    string Version,
    string SourceUrl,
    string Sha256,
    string Format,
    IReadOnlyList<string> Files);