using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>
/// Port: splits Japanese text into a sequence of tokens. MeCab analysis is synchronous pure-CPU work (the
/// dictionary is already in memory) and does not introduce async. null, empty strings, and whitespace-only
/// input return an empty list.
/// </summary>
public interface ITokenizer
{
    IReadOnlyList<Token> Tokenize(string? text);
}