using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Core.Tests.WordGrouping;

public sealed class MorphologicalPitchRulesTests
{
    [Fact]
    public void SingleTokenVolitionalAppliesM1Modification()
    {
        var tabeyou = Verb("食べよう", "タベヨウ", "2", aConType: "C1", aModType: "M1@1");
        var ikou = Verb("行こう", "イコウ", "0", aConType: "C2", aModType: "M1@1");
        var taberu = Verb("食べる", "タベル", "2", aConType: "C1", aModType: "*");

        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([tabeyou]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([ikou]));
        Assert.Null(MorphologicalPitchRules.ResolvePosition([taberu]));
    }

    [Fact]
    public void IchidanAndGodanTeAndTaFormsResolveCorrectly()
    {
        var te = Particle("て", "テ", "動詞%F1,形容詞%F2@-1");
        var de = Particle("で", "デ", "動詞%F1,形容詞%F2@-1");
        var ta = Aux("た", "タ", "助動詞-タ", "動詞%F2@1,形容詞%F4@-2");
        var da = Aux("だ", "ダ", "助動詞-タ", "動詞%F2@1");

        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("食べ", "タベ", "2", aModType: "M4@1"), te]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("食べ", "タベ", "2", aModType: "M4@1"), ta]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("見", "ミ", "1", aModType: "M4@1"), te]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("見", "ミ", "1", aModType: "M4@1"), ta]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Verb("集め", "アツメ", "3", aModType: "M4@1"), te]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("書い", "カイ", "1"), te]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Verb("歩い", "アルイ", "2"), te]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("飲ん", "ノン", "1"), de]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("飲ん", "ノン", "1"), da]));
        Assert.Equal(0, MorphologicalPitchRules.ResolvePosition([Verb("呼ん", "ヨン", "0"), de]));
        Assert.Equal(0, MorphologicalPitchRules.ResolvePosition([Verb("呼ん", "ヨン", "0"), da]));
        Assert.Equal(0, MorphologicalPitchRules.ResolvePosition([Verb("行っ", "イッ", "0"), te]));
        Assert.Equal(0, MorphologicalPitchRules.ResolvePosition([Verb("行っ", "イッ", "0"), ta]));
    }

    [Fact]
    public void M4ShiftRespectsDevoicedVowelsAndSpecialMorae()
    {
        var te = Particle("て", "テ", "動詞%F1,形容詞%F2@-1");

        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Verb("つけ", "ツケ", "2", aModType: "M4@1"), te]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([Verb("助け", "タスケ", "3", aModType: "M4@1"), te]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([Verb("ぞんじ", "ゾンジ", "3", aModType: "M4@1"), te]));
    }

    [Fact]
    public void MultiLayerVerbAuxiliaryChainsResolveCorrectly()
    {
        var taberuStem = Verb("食べ", "タベ", "2", aModType: "M4@1");
        var ikuRenyo = Verb("行き", "イキ", "0");
        var ikuMizen = Verb("行か", "イカ", "0");
        var ikuSokuon = Verb("行っ", "イッ", "0");

        var masu = Aux("ます", "マス", "助動詞-マス", "動詞%F4@1");
        var mase = Aux("ませ", "マセ", "助動詞-マス", "動詞%F4@1");
        var n = Aux("ん", "ン", "助動詞-ヌ", "動詞%F4@0");
        var deshi = Aux("でし", "デシ", "助動詞-デス", "形容詞%F2@-1,動詞%F2@0,名詞%F2@1");
        var ta = Aux("た", "タ", "助動詞-タ", "動詞%F2@1,形容詞%F4@-2");
        var tara = Aux("たら", "タラ", "助動詞-タ", "動詞%F2@1,形容詞%F4@-2", aModType: "M2@1");
        var nai = Aux("ない", "ナイ", "助動詞-ナイ", "動詞%F3@0");
        var nakat = Aux("なかっ", "ナカッ", "助動詞-ナイ", "動詞%F3@0", aModType: "M2@2");
        var tai = Aux("たい", "タイ", "助動詞-タイ", "動詞%F4@1");
        var rareru = Aux("られる", "ラレル", "助動詞-レル", "動詞%F3@2");
        var rare = Aux("られ", "ラレ", "助動詞-レル", "動詞%F3@2", aModType: "M4@1");
        var sase = Aux("させ", "サセ", "下一段-サ行", "動詞%F3@2", aModType: "M4@1");
        var tari = Particle("たり", "タリ", "動詞%F6@1,-1,形容詞%F2@-2");

        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([taberuStem, masu]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([ikuRenyo, masu]));
        Assert.Equal(4, MorphologicalPitchRules.ResolvePosition([taberuStem, mase, n]));
        Assert.Equal(4, MorphologicalPitchRules.ResolvePosition([taberuStem, mase, n, deshi, ta]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([taberuStem, nai]));
        Assert.Equal(0, MorphologicalPitchRules.ResolvePosition([ikuMizen, nai]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([taberuStem, nakat, ta]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([ikuMizen, nakat, ta]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([taberuStem, tai]));
        Assert.Equal(4, MorphologicalPitchRules.ResolvePosition([taberuStem, rareru]));
        Assert.Equal(6, MorphologicalPitchRules.ResolvePosition([taberuStem, sase, rareru]));
        Assert.Equal(5, MorphologicalPitchRules.ResolvePosition([taberuStem, sase, rare, ta]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([ikuSokuon, tara]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([taberuStem, tara]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([ikuSokuon, tari]));
        Assert.Equal(1, MorphologicalPitchRules.ResolvePosition([taberuStem, tari]));
    }

    [Fact]
    public void IAdjectiveConjugationChainsResolveCorrectly()
    {
        var ta = Aux("た", "タ", "助動詞-タ", "動詞%F2@1,形容詞%F4@-2");
        var te = Particle("て", "テ", "動詞%F1,形容詞%F2@-1");
        var desu = Aux("です", "デス", "助動詞-デス", "形容詞%F2@-1,動詞%F2@0,名詞%F2@1");

        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Adj("高かっ", "タカカッ", "2", aModType: "M2@2"), ta]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Adj("赤かっ", "アカカッ", "0", aModType: "M2@2"), ta]));
        Assert.Equal(4, MorphologicalPitchRules.ResolvePosition([Adj("面白かっ", "オモシロカッ", "4", aModType: "M2@2"), ta]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Adj("高く", "タカク", "2"), te]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Adj("赤く", "アカク", "0"), te]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Adj("高い", "タカイ", "2"), desu]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([Adj("赤い", "アカイ", "0"), desu]));
    }

    [Fact]
    public void ProductiveSuffixesCompoundsAndPrefixesResolveCorrectly()
    {
        var ta = Aux("た", "タ", "助動詞-タ", "動詞%F2@1,形容詞%F4@-2");
        var ronri = Noun("論理", "ロンリ", "1", "C1");
        var teki = Suffix("的", "テキ", "形状詞的", "*", "C4");
        var butsuri = Noun("物理", "ブツリ", "1", "C1");
        var gaku = Suffix("学", "ガク", "名詞的", "*", "C3");
        var taberuStem = Verb("食べ", "タベ", "2", aModType: "M4@1");
        var yasui = Suffix("やすい", "ヤスイ", "形容詞的", "2", "C1");
        var sugiru = Verb("すぎる", "スギル", "2", aConType: "C1");
        var sugi = Verb("すぎ", "スギ", "2", aConType: "C1", aModType: "M4@1");
        var oPrefix = Prefix("お", "オ", "P2");
        var sushi = Noun("寿司", "スシ", "1,2", "C3");

        Assert.Equal(0, MorphologicalPitchRules.ResolvePosition([ronri, teki]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([butsuri, gaku]));
        Assert.Equal(4, MorphologicalPitchRules.ResolvePosition([taberuStem, yasui]));
        Assert.Equal(4, MorphologicalPitchRules.ResolvePosition([taberuStem, sugiru]));
        Assert.Equal(3, MorphologicalPitchRules.ResolvePosition([taberuStem, sugi, ta]));
        Assert.Equal(2, MorphologicalPitchRules.ResolvePosition([oPrefix, sushi]));
    }

    [Fact]
    public void FailsClosedOnMissingMetadataUnsupportedRulesOrPostParticleCompounds()
    {
        var te = Particle("て", "テ", "動詞%F1,形容詞%F2@-1");
        var iru = Verb("いる", "イル", "0", aConType: "C4");

        // Missing base accent
        Assert.Null(MorphologicalPitchRules.ResolvePosition([Verb("食べ", "タベ", "*"), te]));
        // Missing function rule
        Assert.Null(MorphologicalPitchRules.ResolvePosition([Verb("食べ", "タベ", "2", aModType: "M4@1"), Particle("て", "テ", "*")]));
        // Unsupported prefix rule (P4)
        Assert.Null(MorphologicalPitchRules.ResolvePosition([Prefix("両", "リョウ", "P4"), Noun("側", "ガワ", "0", "C2")]));
        // Unsupported modification rule
        Assert.Null(MorphologicalPitchRules.ResolvePosition([Verb("食べ", "タベ", "2", aModType: "M9@1")]));
        // Compound C-rule must not apply after a particle/auxiliary like て + いる
        Assert.Null(MorphologicalPitchRules.ResolvePosition([Verb("見", "ミ", "1", aModType: "M4@1"), te, iru]));
    }

    [Fact]
    public void PhonologicalHelpersIdentifySpecialMoraeAndDevoicedVowels()
    {
        Assert.True(PitchAccent.IsSpecialMora("っ"));
        Assert.True(PitchAccent.IsSpecialMora("ん"));
        Assert.True(PitchAccent.IsSpecialMora("ー"));
        Assert.False(PitchAccent.IsSpecialMora("た"));

        Assert.True(PitchAccent.IsDevoicedVowel(["つ", "け"], 0));
        Assert.True(PitchAccent.IsDevoicedVowel(["た", "す", "け"], 1));
        Assert.False(PitchAccent.IsDevoicedVowel(["あ", "つ", "め"], 1));
        Assert.False(PitchAccent.IsDevoicedVowel(["た", "べ"], 0));
    }

    private static Token Verb(string surface, string reading, string aType, string aConType = "C1", string aModType = "*")
        => new(surface, surface, surface, reading, reading, reading,
            new("動詞", "一般", "", ""), "下一段", "連用形", aType, 0, aConType, aModType);

    private static Token Adj(string surface, string reading, string aType, string aConType = "C1", string aModType = "*")
        => new(surface, surface, surface, reading, reading, reading,
            new("形容詞", "一般", "", ""), "形容詞", "連用形", aType, 0, aConType, aModType);

    private static Token Noun(string surface, string reading, string aType, string aConType)
        => new(surface, surface, surface, reading, reading, reading,
            new("名詞", "普通名詞", "", ""), "*", "*", aType, 0, aConType, "*");

    private static Token Aux(string surface, string reading, string cType, string aConType, string aModType = "*")
        => new(surface, surface, surface, reading, reading, reading,
            new("助動詞", "*", "", ""), cType, "終止形", "*", 0, aConType, aModType);

    private static Token Particle(string surface, string reading, string aConType)
        => new(surface, surface, surface, reading, reading, reading,
            new("助詞", "接続助詞", "", ""), "*", "*", "*", 0, aConType, "*");

    private static Token Suffix(string surface, string reading, string pos2, string aType, string aConType)
        => new(surface, surface, surface, reading, reading, reading,
            new("接尾辞", pos2, "", ""), "*", "*", aType, 0, aConType, "*");

    private static Token Prefix(string surface, string reading, string aConType)
        => new(surface, surface, surface, reading, reading, reading,
            new("接頭辞", "*", "", ""), "*", "*", "*", 0, aConType, "*");
}
