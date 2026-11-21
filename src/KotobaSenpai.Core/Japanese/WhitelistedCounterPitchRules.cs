namespace KotobaSenpai.Core.Japanese;

internal static class WhitelistedCounterPitchRules
{
    private sealed record Counter(string Surface, string Reading);

    private static readonly Counter[] Counters =
    [
        new("週間", "しゅうかん"), new("時間", "じかん"), new("年生", "ねんせい"),
        new("ヶ月", "かげつ"), new("か月", "かげつ"), new("ヵ月", "かげつ"),
        new("カ月", "かげつ"), new("箇月", "かげつ"), new("曜日", "ようび"),
    ];

    private static readonly string[] StandardNumbers =
        ["", "いち", "に", "さん", "よん", "ご", "ろく", "なな", "はち", "きゅう"];

    internal static string? DeriveReading(string surface)
        => Match(surface) is { } match ? match.PrefixReading + match.Counter.Reading : null;

    internal static int? ResolvePosition(string surface, string reading)
    {
        if (Match(surface) is not { } match || reading.Length <= match.Counter.Reading.Length
            || !reading.EndsWith(match.Counter.Reading, StringComparison.Ordinal))
            return null;

        return PitchAccent.SplitMorae(reading[..^match.Counter.Reading.Length]).Count + 1;
    }

    private static (Counter Counter, string PrefixReading)? Match(string surface)
    {
        foreach (var counter in Counters)
        {
            if (!surface.EndsWith(counter.Surface, StringComparison.Ordinal) || surface.Length == counter.Surface.Length)
                continue;

            var prefix = surface[..^counter.Surface.Length];
            if (prefix == "何")
                return (counter, "なん");
            if (counter.Surface == "曜日")
            {
                var day = prefix switch
                {
                    "月" => "げつ", "火" => "か", "水" => "すい", "木" => "もく",
                    "金" => "きん", "土" => "ど", "日" => "にち", _ => null,
                };
                return day is null ? null : (counter, day);
            }
            if (!JapaneseNumerals.TryParse(prefix, out var number))
                return null;

            return (counter, ReadNumber(number, counter));
        }
        return null;
    }

    private static string ReadNumber(int number, Counter counter)
    {
        var tens = number / 10;
        var units = number % 10;
        var tensPrefix = tens > 1 ? StandardNumbers[tens] : "";
        var prefix = tens == 0 ? "" : tensPrefix + "じゅう";
        return (counter.Surface, units) switch
        {
            ("週間", 0 or 1 or 8) => Sokuon(),
            ("時間", 4) or ("年生", 4) => prefix + "よ",
            ("時間", 7) => prefix + "しち",
            ("時間", 9) => prefix + "く",
            _ when counter.Reading == "かげつ" && units is 0 or 1 or 6 or 8 => Sokuon(),
            _ => prefix + StandardNumbers[units],
        };

        string Sokuon() => units switch
        {
            0 => tensPrefix + "じゅっ",
            1 => prefix + "いっ",
            6 => prefix + "ろっ",
            _ => prefix + "はっ",
        };
    }
}
