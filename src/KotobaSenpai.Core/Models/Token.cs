namespace KotobaSenpai.Core.Models;

/// <summary>
/// UniDic 四级词性（pos1..pos4）。字段缺失时保留空字符串，不改变列表长度。
/// </summary>
public sealed record PartsOfSpeech(string Pos1, string Pos2, string Pos3, string Pos4);

/// <summary>
/// 日语分词结果的一个词元。镜像 UniDic unidic22 字段，供后续查词/振假名/语法解释使用。
/// <list type="bullet">
/// <item><see cref="Surface"/> 词面（出现形）；</item>
/// <item><see cref="Lemma"/> UniDic 語彙素（辞书形）；</item>
/// <item><see cref="OrthBase"/> 書字形基本形；</item>
/// <item><see cref="Reading"/> 仮名形出現形（kana）；</item>
/// <item><see cref="BaseReading"/> 仮名形基本形（kanaBase）；</item>
/// <item><see cref="Pronunciation"/> 発音形出現形（pron）；</item>
/// </list>
/// <see cref="AType"/> 是 UniDic 原始音高重音字段，可能为空或含多值/引号语义，不能解释为 Doki 的最终音高。
/// <see cref="StartOffset"/> 是输入 .NET 字符串中的 UTF-16 code-unit 起点（非字节、非 code-point）。
/// </summary>
public sealed record Token
{
    public Token(
        string surface,
        string lemma,
        string orthBase,
        string reading,
        string baseReading,
        string pronunciation,
        PartsOfSpeech partsOfSpeech,
        string conjugationType,
        string conjugationForm,
        string aType,
        int startOffset)
    {
        if (string.IsNullOrEmpty(surface))
            throw new ArgumentException("Token surface must not be empty.", nameof(surface));
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset), "Token start offset must be non-negative.");

        Surface = surface;
        Lemma = lemma;
        OrthBase = orthBase;
        Reading = reading;
        BaseReading = baseReading;
        Pronunciation = pronunciation;
        PartsOfSpeech = partsOfSpeech;
        ConjugationType = conjugationType;
        ConjugationForm = conjugationForm;
        AType = aType;
        StartOffset = startOffset;
    }

    /// <summary>词面（出现形）。</summary>
    public string Surface { get; }

    /// <summary>UniDic 語彙素（辞书形，用于查词典）。</summary>
    public string Lemma { get; }

    /// <summary>書字形基本形。</summary>
    public string OrthBase { get; }

    /// <summary>仮名形出現形（kana）。</summary>
    public string Reading { get; }

    /// <summary>仮名形基本形（kanaBase）。</summary>
    public string BaseReading { get; }

    /// <summary>発音形出現形（pron）。</summary>
    public string Pronunciation { get; }

    /// <summary>四级词性（pos1..pos4，缺失为空字符串）。</summary>
    public PartsOfSpeech PartsOfSpeech { get; }

    /// <summary>活用型（cType）。</summary>
    public string ConjugationType { get; }

    /// <summary>活用形（cForm）。</summary>
    public string ConjugationForm { get; }

    /// <summary>UniDic 原始音高重音字段（aType），非最终音高。</summary>
    public string AType { get; }

    /// <summary>输入字符串中的 UTF-16 code-unit 起始偏移。</summary>
    public int StartOffset { get; }
}