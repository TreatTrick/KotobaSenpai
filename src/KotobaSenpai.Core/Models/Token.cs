using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// UniDic's four-level part of speech (pos1..pos4). Missing fields keep an empty string without changing the
/// list length.
/// </summary>
public sealed record PartsOfSpeech(string Pos1, string Pos2, string Pos3, string Pos4);

/// <summary>
/// One token from Japanese tokenization. Mirrors the UniDic unidic22 fields for later lookup/furigana/grammar
/// explanation.
/// <list type="bullet">
/// <item><see cref="Surface"/> surface (occurring form);</item>
/// <item><see cref="Lemma"/> UniDic 語彙素 (dictionary form);</item>
/// <item><see cref="OrthBase"/> 書字形基本形 (base orthographic form);</item>
/// <item><see cref="Reading"/> 仮名形出現形 (kana, occurring form);</item>
/// <item><see cref="BaseReading"/> 仮名形基本形 (kanaBase, base form);</item>
/// <item><see cref="Pronunciation"/> 発音形出現形 (pron, occurring form);</item>
/// </list>
/// <see cref="AType"/> is UniDic's raw pitch-accent field; it may be empty or carry multi-value/quote semantics
/// and must not be interpreted as Doki's final pitch.
/// <see cref="StartOffset"/> is the UTF-16 code-unit start in the input .NET string (not bytes, not code points).
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
        PitchAccentPosition = PitchAccent.ParsePosition(aType, reading);
        StartOffset = startOffset;
    }

    /// <summary>Surface (occurring form).</summary>
    public string Surface { get; }

    /// <summary>UniDic 語彙素 (dictionary form, used for dictionary lookup).</summary>
    public string Lemma { get; }

    /// <summary>書字形基本形 (base orthographic form).</summary>
    public string OrthBase { get; }

    /// <summary>仮名形出現形 (kana, occurring form).</summary>
    public string Reading { get; }

    /// <summary>仮名形基本形 (kanaBase, base form).</summary>
    public string BaseReading { get; }

    /// <summary>発音形出現形 (pron, occurring form).</summary>
    public string Pronunciation { get; }

    /// <summary>Four-level part of speech (pos1..pos4, empty string when missing).</summary>
    public PartsOfSpeech PartsOfSpeech { get; }

    /// <summary>Conjugation type (cType).</summary>
    public string ConjugationType { get; }

    /// <summary>Conjugation form (cForm).</summary>
    public string ConjugationForm { get; }

    /// <summary>UniDic's raw pitch-accent field (aType), not the final pitch.</summary>
    public string AType { get; }

    /// <summary>Normalized UniDic pitch nucleus: 0=heiban, 1=atamadaka, N=drop after mora N.</summary>
    public int? PitchAccentPosition { get; }

    /// <summary>UTF-16 code-unit start offset in the input string.</summary>
    public int StartOffset { get; }
}
