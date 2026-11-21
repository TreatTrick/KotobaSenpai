using System.Globalization;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.WordGrouping;

/// <summary>
/// Offline UniDic morphological pitch combiner using token <c>aType</c>, <c>aModType</c>, and <c>aConType</c>.
/// Fails closed (returns <c>null</c>) whenever required metadata is missing or an unsupported rule is encountered.
/// </summary>
internal static class MorphologicalPitchRules
{
    private enum ModKind { M1, M2, M4 }
    private readonly record struct ModRule(ModKind Kind, int Value);

    private enum FunctionKind { F1, F2, F3, F4, F5, F6 }
    private readonly record struct FunctionRule(FunctionKind Kind, int Primary = 0, int Secondary = 0);

    private enum CompoundKind { C1, C2, C3, C4, C5 }

    private enum PrefixKind { P1, P2, P6, P13, P14 }

    internal static int? ResolvePosition(IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
            return null;

        var tokenMorae = new IReadOnlyList<string>[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var reading = Kana.ToHiragana(tokens[i].Reading);
            if (!JapaneseText.IsHiraganaReading(reading))
                return null;
            var morae = PitchAccent.SplitMorae(reading);
            if (morae.Count == 0)
                return null;
            tokenMorae[i] = morae;
        }

        // For a single token, only override its lexical pitch when UniDic explicitly marks a conjugation shift.
        if (tokens.Count == 1)
        {
            if (tokens[0].PitchAccentPosition is not int singleBase
                || !HasValue(tokens[0].AModType)
                || !TryParseModification(tokens[0].AModType, out var singleMod))
                return null;

            return ApplyModification(singleMod, tokenMorae[0], tokenMorae[0].Count, singleBase);
        }

        var moraeBuffer = new List<string>(tokenMorae.Sum(m => m.Count));
        int accent;
        string category;
        int startIndex;

        if (tokens[0].PartsOfSpeech.Pos1 == "接頭辞")
        {
            if (!TryInitializePrefix(tokens[0], tokenMorae[0], tokens[1], tokenMorae[1],
                    moraeBuffer, out accent, out category))
                return null;
            startIndex = 2;
        }
        else
        {
            if (tokens[0].PitchAccentPosition is not int baseAccent || MapCategory(tokens[0]) is not { } baseCategory)
                return null;

            moraeBuffer.AddRange(tokenMorae[0]);
            accent = baseAccent;
            category = baseCategory;
            if (!TryApplyOptionalModification(tokens[0].AModType, moraeBuffer, ref accent))
                return null;
            startIndex = 1;
        }

        var previousWasFunctionWord = false;
        for (var i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var nextMorae = tokenMorae[i];
            var n1 = moraeBuffer.Count;
            var n2 = nextMorae.Count;

            if (token.PartsOfSpeech.Pos1 is "助動詞" or "助詞")
            {
                if (!TryFindFunctionRule(token.AConType, category, out var functionRule))
                    return null;

                var combined = ApplyFunctionRule(functionRule, n1, n2, accent, token, category);
                moraeBuffer.AddRange(nextMorae);
                if (combined < 0 || combined > moraeBuffer.Count)
                    return null;
                accent = combined;
                previousWasFunctionWord = true;

                if (!TryApplyOptionalModification(token.AModType, moraeBuffer, ref accent))
                    return null;
            }
            else if (!previousWasFunctionWord
                && (token.PartsOfSpeech.Pos1 == "接尾辞" || TryParseCompoundRule(token.AConType, out _)))
            {
                if (!TryParseCompoundRule(token.AConType, out var compoundKind))
                    return null;

                var combined = ApplyCompoundRule(compoundKind, moraeBuffer, n1, accent, token);
                if (combined is null)
                    return null;

                moraeBuffer.AddRange(nextMorae);
                if (combined.Value < 0 || combined.Value > moraeBuffer.Count)
                    return null;
                accent = combined.Value;
                category = MapCategory(token) ?? category;
                previousWasFunctionWord = false;

                if (!TryApplyOptionalModification(token.AModType, moraeBuffer, ref accent))
                    return null;
            }
            else
            {
                return null;
            }
        }

        return accent >= 0 && accent <= moraeBuffer.Count ? accent : null;
    }

    private static bool TryInitializePrefix(
        Token prefix,
        IReadOnlyList<string> prefixMorae,
        Token stem,
        IReadOnlyList<string> stemMorae,
        List<string> moraeBuffer,
        out int accent,
        out string category)
    {
        accent = 0;
        category = string.Empty;
        if (!TryParsePrefixRule(prefix.AConType, out var prefixKind)
            || stem.PitchAccentPosition is not int m2
            || MapCategory(stem) is not { } stemCategory)
            return false;

        var n1 = prefixMorae.Count;
        var n2 = stemMorae.Count;
        int? combined = prefixKind switch
        {
            PrefixKind.P1 => m2 == 0 || m2 == n2 ? 0 : n1 + m2,
            PrefixKind.P2 or PrefixKind.P14 => m2 == 0 ? 0 : n1 + m2,
            PrefixKind.P6 => 0,
            PrefixKind.P13 => prefix.PitchAccentPosition,
            _ => null,
        };
        if (combined is null)
            return false;

        moraeBuffer.AddRange(prefixMorae);
        moraeBuffer.AddRange(stemMorae);
        if (combined.Value < 0 || combined.Value > moraeBuffer.Count)
            return false;

        accent = combined.Value;
        category = stemCategory;
        return TryApplyOptionalModification(stem.AModType, moraeBuffer, ref accent);
    }

    private static bool TryApplyOptionalModification(string rawModType, IReadOnlyList<string> morae, ref int accent)
    {
        if (!HasValue(rawModType))
            return true;
        if (!TryParseModification(rawModType, out var rule))
            return false;

        var modified = ApplyModification(rule, morae, morae.Count, accent);
        if (modified is null)
            return false;

        accent = modified.Value;
        return true;
    }

    private static int? ApplyModification(ModRule rule, IReadOnlyList<string> morae, int moraCount, int currentAccent)
    {
        int result;
        switch (rule.Kind)
        {
            case ModKind.M1:
                result = moraCount - rule.Value;
                return result >= 1 && result <= moraCount ? result : null;

            case ModKind.M2:
                if (currentAccent != 0)
                    return currentAccent;
                result = moraCount - rule.Value;
                return result >= 1 && result <= moraCount ? result : null;

            case ModKind.M4:
                if (currentAccent == 0)
                    return 0;
                if (currentAccent == 1)
                    return 1;
                var shifted = Math.Max(1, currentAccent - rule.Value);
                if (PitchAccent.IsDevoicedVowel(morae, shifted - 1))
                    return currentAccent;
                if (shifted > 1 && PitchAccent.IsSpecialMora(morae[shifted - 1]))
                    shifted--;
                return shifted;

            default:
                return null;
        }
    }

    private static int ApplyFunctionRule(
        FunctionRule rule, int n1, int n2, int m1, Token token, string category)
    {
        switch (rule.Kind)
        {
            case FunctionKind.F1:
                return m1;

            case FunctionKind.F2:
                if (m1 != 0)
                    return m1;
                var candidate = n1 + rule.Primary;
                // Verbal past-tense た/だ (single mora) flattens instead of placing an accent on the final mora.
                if (category == "動詞" && token.ConjugationType == "助動詞-タ" && n2 == 1 && candidate == n1 + n2)
                    return 0;
                return candidate;

            case FunctionKind.F3:
                return m1 == 0 ? 0 : n1 + rule.Primary;

            case FunctionKind.F4:
                return n1 + rule.Primary;

            case FunctionKind.F5:
                return 0;

            case FunctionKind.F6:
                return m1 == 0
                    ? n1 + rule.Primary
                    : (rule.Secondary == -1 ? m1 : n1 + rule.Secondary);

            default:
                return -1;
        }
    }

    private static int? ApplyCompoundRule(
        CompoundKind kind, IReadOnlyList<string> prefixMorae, int n1, int m1, Token suffix)
        => kind switch
        {
            CompoundKind.C1 => suffix.PitchAccentPosition is int m2 and > 0 ? n1 + m2 : null,
            CompoundKind.C2 => n1 + 1,
            CompoundKind.C3 => n1 > 1 && PitchAccent.IsSpecialMora(prefixMorae[n1 - 1]) ? n1 - 1 : n1,
            CompoundKind.C4 => 0,
            CompoundKind.C5 => m1,
            _ => null,
        };

    private static bool TryParseModification(string raw, out ModRule rule)
    {
        rule = default;
        if (!HasValue(raw))
            return false;

        var trimmed = raw.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 1 || at == trimmed.Length - 1)
            return false;

        if (!int.TryParse(trimmed[(at + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < 0)
            return false;

        var kind = trimmed[..at] switch
        {
            "M1" => (ModKind?)ModKind.M1,
            "M2" => ModKind.M2,
            "M4" => ModKind.M4,
            _ => null,
        };
        if (kind is null)
            return false;

        rule = new ModRule(kind.Value, value);
        return true;
    }

    private static bool TryFindFunctionRule(string rawAConType, string category, out FunctionRule rule)
    {
        rule = default;
        if (!HasValue(rawAConType))
            return false;

        // Entries may contain comma-separated POS%Rule specs where F6 itself contains a comma (e.g. 動詞%F6@1,-1,形容詞%F2@-2).
        var segments = SplitConTypeEntries(rawAConType);
        foreach (var segment in segments)
        {
            var percent = segment.IndexOf('%');
            if (percent <= 0 || percent == segment.Length - 1)
                continue;

            var pos = segment[..percent].Trim();
            if (!string.Equals(pos, category, StringComparison.Ordinal))
                continue;

            return TryParseFunctionRule(segment[(percent + 1)..].Trim(), out rule);
        }

        return false;
    }

    private static List<string> SplitConTypeEntries(string raw)
    {
        var entries = new List<string>();
        foreach (var part in raw.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            // Rejoin the second integer parameter of F6@a,b when split by comma.
            if (entries.Count > 0 && !trimmed.Contains('%')
                && int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && entries[^1].Contains("F6@", StringComparison.Ordinal))
            {
                entries[^1] += "," + trimmed;
            }
            else
            {
                entries.Add(trimmed);
            }
        }
        return entries;
    }

    private static bool TryParseFunctionRule(string spec, out FunctionRule rule)
    {
        rule = default;
        if (spec == "F1")
        {
            rule = new FunctionRule(FunctionKind.F1);
            return true;
        }
        if (spec == "F5")
        {
            rule = new FunctionRule(FunctionKind.F5);
            return true;
        }

        var at = spec.IndexOf('@');
        if (at <= 1 || at == spec.Length - 1)
            return false;

        var prefix = spec[..at];
        var args = spec[(at + 1)..];
        if (prefix == "F6")
        {
            var parts = args.Split(',');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var primary)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var secondary))
            {
                rule = new FunctionRule(FunctionKind.F6, primary, secondary);
                return true;
            }
            return false;
        }

        if (!int.TryParse(args, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
            return false;

        var kind = prefix switch
        {
            "F2" => (FunctionKind?)FunctionKind.F2,
            "F3" => FunctionKind.F3,
            "F4" => FunctionKind.F4,
            _ => null,
        };
        if (kind is null)
            return false;

        rule = new FunctionRule(kind.Value, offset);
        return true;
    }

    private static bool TryParseCompoundRule(string raw, out CompoundKind kind)
    {
        kind = default;
        if (!HasValue(raw))
            return false;

        var parsed = raw.Trim() switch
        {
            "C1" => (CompoundKind?)CompoundKind.C1,
            "C2" => CompoundKind.C2,
            "C3" => CompoundKind.C3,
            "C4" => CompoundKind.C4,
            "C5" => CompoundKind.C5,
            _ => null,
        };
        if (parsed is null)
            return false;

        kind = parsed.Value;
        return true;
    }

    private static bool TryParsePrefixRule(string raw, out PrefixKind kind)
    {
        kind = default;
        if (!HasValue(raw))
            return false;

        var parsed = raw.Trim() switch
        {
            "P1" => (PrefixKind?)PrefixKind.P1,
            "P2" => PrefixKind.P2,
            "P6" => PrefixKind.P6,
            "P13" => PrefixKind.P13,
            "P14" => PrefixKind.P14,
            _ => null,
        };
        if (parsed is null)
            return false;

        kind = parsed.Value;
        return true;
    }

    private static string? MapCategory(Token token) => token.PartsOfSpeech.Pos1 switch
    {
        "動詞" => "動詞",
        "形容詞" => "形容詞",
        "形状詞" => "形状詞",
        "名詞" or "代名詞" => "名詞",
        "副詞" => "副詞",
        "接尾辞" => token.PartsOfSpeech.Pos2 switch
        {
            "形容詞的" => "形容詞",
            "形状詞的" => "形状詞",
            "動詞的" => "動詞",
            "名詞的" => "名詞",
            _ => null,
        },
        _ => null,
    };

    private static bool HasValue(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim() != "*";
}
