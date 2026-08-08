namespace KotobaSenpai.Core.Japanese;

/// <summary>
/// UniDic 词典资产冻结清单：Doki 兼容的 <c>unidic-py</c> 构建 <c>3.1.0+2021-08-31</c>。
/// 词典数据 BSD 许可（NINJAL），与 LibNMeCab 的 GPL/LGPL 双许可分开处理。
/// 仅在安装/校验时使用；分词器与安装器经此共享同一固定版本与格式约定。
/// </summary>
public static class UniDicAssets
{
    /// <summary>固定版本（对齐 Doki Windows release 使用的 unidic-py 构建）。</summary>
    public const string Version = "3.1.0+2021-08-31";

    /// <summary>固定下载 URL（禁止解析 latest 别名或未校验镜像）。</summary>
    public const string SourceUrl = "https://cotonoha-dic.s3-ap-northeast-1.amazonaws.com/unidic-3.1.0.zip";

    /// <summary>已压缩词典包 SHA-256（实测 2026-08-08，下载自 <see cref="SourceUrl"/>）。</summary>
    public const string Sha256 = "638718c4c63625ab300de4c92c67925d54c0e9e3830009eaa992f29819d59c43";

    /// <summary>MeCab feature 格式（UniDic 2.2+ 统一为 unidic22）。</summary>
    public const string Format = "unidic22";

    /// <summary>LibNMeCab 0.10.2 运行时必需的四个文件（其余文件如 model.bin 缺省不影响加载，随包原样保留）。</summary>
    public static readonly string[] RequiredRuntimeFiles = ["char.bin", "matrix.bin", "sys.dic", "unk.dic"];

    /// <summary>安装器写入的 manifest 文件名。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>词典自带的版本文件（内容形如 "unidic-3.1.0+2021-08-31"）。</summary>
    public const string VersionFileName = "version";

    /// <summary>词典自带的 MeCab 配置（须含 output-format-type = unidic22）。</summary>
    public const string DicrcFileName = "dicrc";
}

/// <summary>安装器写入安装目录的 manifest 内容（版本/来源/哈希/格式/文件集）。</summary>
public sealed record UniDicManifest(
    string Version,
    string SourceUrl,
    string Sha256,
    string Format,
    IReadOnlyList<string> Files);