using KotobaSenpai.Core.Session;

namespace KotobaSenpai.Core.Tests.Session;

/// <summary>
/// The single source of truth for "is this snapshot still valid". Two transitions exist and they mean
/// different things: <see cref="SessionEpoch.Advance"/> invalidates cached analysis, while a publication
/// rotation only supersedes the UI output, so the tests lock both semantics down separately.
/// </summary>
public sealed class SessionEpochTests
{
    [Fact]
    public void Advance_invalidates_every_snapshot_taken_before_it()
    {
        var epoch = new SessionEpoch();
        var before = epoch.Capture();
        Assert.True(epoch.IsCurrent(before));

        var after = epoch.Advance();

        Assert.False(epoch.IsCurrent(before));
        Assert.False(epoch.IsCurrent(before.Value));
        Assert.True(epoch.IsCurrent(after));
        Assert.NotEqual(before.Value, after.Value);
    }

    [Fact]
    public void Rotating_the_publication_keeps_the_epoch_valid_for_cached_analysis()
    {
        var epoch = new SessionEpoch();
        var observed = epoch.Capture();

        Assert.True(epoch.TryAdvancePublication(observed, out var current));

        // Same epoch value: sentence analysis cached under it stays reusable.
        Assert.Equal(observed.Value, current.Value);
        Assert.True(epoch.IsCurrent(observed.Value));
        Assert.True(epoch.IsCurrent(current));
        // Different publication identity: output produced before the rotation must not win.
        Assert.False(epoch.IsCurrent(observed));
    }

    [Fact]
    public void Stale_publication_attempts_are_rejected_without_advancing_anything()
    {
        var epoch = new SessionEpoch();
        var stale = epoch.Capture();
        epoch.Advance();
        var live = epoch.Capture();

        Assert.False(epoch.TryAdvancePublication(stale, out var current));

        Assert.Equal(live.Value, current.Value);
        Assert.True(epoch.IsCurrent(live));
        Assert.True(epoch.IsCurrent(current));
    }

    [Fact]
    public void Capture_is_stable_until_a_transition_happens()
    {
        var epoch = new SessionEpoch();

        Assert.True(epoch.IsCurrent(epoch.Capture()));
        Assert.True(epoch.IsCurrent(epoch.Capture()));
    }

    [Fact]
    public void Only_one_of_many_concurrent_publication_rotations_from_the_same_snapshot_wins()
    {
        var epoch = new SessionEpoch();
        var observed = epoch.Capture();

        var results = Enumerable.Range(0, 16).AsParallel()
            .Select(index => epoch.TryAdvancePublication(observed, out _))
            .ToArray();

        Assert.Equal(1, results.Count(succeeded => succeeded));
        Assert.False(epoch.IsCurrent(observed));
    }

    [Fact]
    public void Concurrent_advances_stay_monotonic_and_never_revalidate_an_older_snapshot()
    {
        const int workers = 8;
        const int advancesPerWorker = 250;
        var epoch = new SessionEpoch();

        Parallel.For(0, workers, _ =>
        {
            for (var i = 0; i < advancesPerWorker; i++)
            {
                var snapshot = epoch.Capture();
                epoch.Advance();
                Assert.False(epoch.IsCurrent(snapshot));
            }
        });

        Assert.Equal(workers * advancesPerWorker, epoch.Capture().Value);
    }
}
