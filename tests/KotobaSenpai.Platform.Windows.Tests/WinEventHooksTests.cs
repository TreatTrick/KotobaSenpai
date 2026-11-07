using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class WinEventHooksTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void Registration_failure_releases_every_acquired_handle(int failureIndex, bool throws)
    {
        var acquired = new List<nint>();
        var released = new List<nint>();
        var index = 0;
        nint Register(uint first, uint last, WindowNativeMethods.WinEventDelegate callback)
        {
            var current = index++;
            if (current == failureIndex)
            {
                if (throws) throw new InvalidOperationException("Registration failed.");
                return nint.Zero;
            }
            var handle = (nint)(current + 1);
            acquired.Add(handle);
            return handle;
        }

        Assert.Throws<InvalidOperationException>(() => new WinEventHooks(Callback, (nint)42, null,
            Register, handle => { released.Add(handle); return true; }));

        Assert.Equal(acquired, released);
        Assert.DoesNotContain(nint.Zero, released);
    }

    [Fact]
    public void Disposing_twice_releases_all_six_hooks_once()
    {
        var ranges = new List<(uint, uint)>();
        var released = new List<nint>();
        var hooks = new WinEventHooks(Callback, (nint)42, null,
            (first, last, callback) => { ranges.Add((first, last)); return (nint)ranges.Count; },
            handle => { released.Add(handle); return true; });

        Assert.True(hooks.IsRegistered);
        Assert.Equal([(0x800Bu, 0x800Bu), (0x0016u, 0x0017u), (0x8001u, 0x8001u),
            (0x8002u, 0x8003u), (0x8004u, 0x8004u), (0x0003u, 0x0003u)], ranges);
        hooks.Dispose();
        hooks.Dispose();

        Assert.False(hooks.IsRegistered);
        Assert.Equal(Enumerable.Range(1, 6).Select(value => (nint)value), released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unregister_failure_does_not_prevent_releasing_the_remaining_hooks(bool throws)
    {
        var index = 0;
        var attempted = new List<nint>();
        var hooks = new WinEventHooks(Callback, (nint)42, null,
            (first, last, callback) => (nint)(++index),
            handle =>
            {
                attempted.Add(handle);
                if (handle != (nint)2) return true;
                if (throws) throw new InvalidOperationException("Unregister failed.");
                return false;
            });

        hooks.Dispose();
        hooks.Dispose();

        Assert.False(hooks.IsRegistered);
        Assert.Equal(Enumerable.Range(1, 6).Select(value => (nint)value), attempted);
    }

    private static void Callback(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime) { }
}
