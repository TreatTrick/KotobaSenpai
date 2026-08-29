using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Core.Tests.TargetWindow;

public sealed class TargetWindowSnapshotTests
{
    [Fact]
    public void Snapshot_is_capturable_when_visible_restored_and_unobstructed()
    {
        var bounds = new ScreenRect(0, 0, 100, 100);

        Assert.True(new TargetWindowSnapshot((nint)1, "target", bounds, 1, true, false, true).IsCapturable);
        Assert.True(new TargetWindowSnapshot((nint)1, "target", bounds, 1, true, false, false).IsCapturable);
        Assert.True(new TargetWindowSnapshot((nint)1, "target", bounds, 1, true, false, false).IsRenderable);
        Assert.False(new TargetWindowSnapshot((nint)1, "target", bounds, 1, false, false, true).IsCapturable);
        Assert.False(new TargetWindowSnapshot((nint)1, "target", bounds, 1, true, true, true).IsCapturable);
        Assert.False(new TargetWindowSnapshot((nint)1, "target", bounds, 1, true, false, true, true).IsCapturable);
        Assert.False(new TargetWindowSnapshot((nint)1, "target", bounds, 1, true, false, true, false, false).IsCapturable);
    }
}
