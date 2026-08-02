using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.SeedWork;

namespace KotobaSenpai.Core.Models.Rules;

/// <summary>覆盖层会话必须绑定到一个非空的目标窗口。</summary>
internal sealed class OverlayTargetMustBeSpecifiedRule : IBusinessRule
{
    private readonly WindowTarget? _target;

    public OverlayTargetMustBeSpecifiedRule(WindowTarget? target) => _target = target;

    public string Message => "Overlay session target must be specified.";

    public string ErrorCode => ErrorCodes.OverlayTargetNotSpecified;

    public bool IsBroken() => _target is null;
}
