namespace KotobaSenpai.Core.SeedWork;

/// <summary>领域不变量规则；<see cref="IsBroken"/> 返回真时表示当前状态违反规则。</summary>
public interface IBusinessRule
{
    bool IsBroken();

    string Message { get; }
}
