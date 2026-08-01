using KotobaSenpai.Core.SeedWork;

namespace KotobaSenpai.Core.Models.Rules;

/// <summary>覆盖层会话必须绑定到一个非空的目标窗口。</summary>
internal sealed class OverlayTargetMustBeSpecifiedRule : IBusinessRule
{
    private readonly WindowTarget? _target;

    public OverlayTargetMustBeSpecifiedRule(WindowTarget? target) => _target = target;

    public string Message => "覆盖层会话必须指定目标窗口。";

    public bool IsBroken() => _target is null;
}
