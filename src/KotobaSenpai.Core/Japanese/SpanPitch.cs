namespace KotobaSenpai.Core.Japanese;

public enum SpanPitchSource
{
    ManualOverride,
    Dictionary,
    CounterRule,
    MorphologicalRule,
}

public sealed record SpanPitch(string Reading, IReadOnlyList<int> Positions, SpanPitchSource Source);
