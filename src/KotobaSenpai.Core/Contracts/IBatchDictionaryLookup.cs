using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>为一次识别批量查询候选表单，避免每个候选单独打开词典连接。</summary>
public interface IBatchDictionaryLookup
{
    /// <summary>返回字典的键与传入表单一致；实现可在内部做假名归一化。</summary>
    IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> LookupForms(
        IReadOnlyCollection<string> forms);
}
