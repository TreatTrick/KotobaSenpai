namespace KotobaSenpai.Core.Models;

/// <summary>
/// token 引用 ID 的值对象：强类型封装 <c>l{LineId}:t{LineTokenIndex}</c> 符号，避免字符串漂移。
/// 线格式与 LLM 契约保持 {@code l0:t3}，但应用内部一律用此类型，格式/解析集中在此一处。
/// </summary>
public readonly record struct SentenceTokenId(int LineId, int LineTokenIndex)
{
    /// <summary>线格式：<c>l0:t3</c>。</summary>
    public override string ToString() => $"l{LineId}:t{LineTokenIndex}";

    public static SentenceTokenId Parse(string value)
    {
        if (!TryParse(value, out var id))
            throw new FormatException($"Invalid token id '{value}'.");
        return id;
    }

    /// <summary>解析 <c>l{line}:t{token}</c>；格式不符或下标为负返回 false。</summary>
    public static bool TryParse(string? value, out SentenceTokenId id)
    {
        id = default;
        if (value is null || value.Length < 4 || value[0] != 'l')
            return false;
        var colon = value.IndexOf(':');
        if (colon < 2 || colon + 2 >= value.Length || value[colon + 1] != 't')
            return false;
        if (!int.TryParse(value.AsSpan(1, colon - 1), out var line) ||
            !int.TryParse(value.AsSpan(colon + 2), out var token) ||
            line < 0 || token < 0)
        {
            return false;
        }
        id = new SentenceTokenId(line, token);
        return true;
    }
}