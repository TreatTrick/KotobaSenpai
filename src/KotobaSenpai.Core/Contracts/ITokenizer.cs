using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>
/// 端口：把日文文本切分为词元序列。MeCab 分析是同步纯 CPU 工作（词典已在内存），
/// 不引入 async。null、空字符串和仅含空白的输入返回空列表。
/// </summary>
public interface ITokenizer
{
    IReadOnlyList<Token> Tokenize(string? text);
}