namespace KotobaSenpai.Core.Japanese;

public enum SpanPitchSource
{
    ManualOverride,
    Dictionary,
    CounterRule,
}

public sealed record SpanPitch(string Reading, IReadOnlyList<int> Positions, SpanPitchSource Source);
