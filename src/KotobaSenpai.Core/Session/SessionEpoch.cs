namespace KotobaSenpai.Core.Session;

/// <summary>Owns session validity and the current publication within that session.</summary>
public sealed class SessionEpoch
{
    private readonly object _gate = new();
    private long _value;
    private object _publication = new();

    public SessionEpochSnapshot Capture()
    {
        lock (_gate)
            return new SessionEpochSnapshot(_value, _publication);
    }

    internal SessionEpochSnapshot Advance()
    {
        lock (_gate)
        {
            _value++;
            _publication = new object();
            return new SessionEpochSnapshot(_value, _publication);
        }
    }

    public bool IsCurrent(long value)
    {
        lock (_gate)
            return value == _value;
    }

    public bool IsCurrent(SessionEpochSnapshot snapshot)
    {
        lock (_gate)
            return snapshot.Value == _value && ReferenceEquals(snapshot.Publication, _publication);
    }

    internal bool TryAdvancePublication(SessionEpochSnapshot observed, out SessionEpochSnapshot current)
    {
        lock (_gate)
        {
            if (observed.Value != _value || !ReferenceEquals(observed.Publication, _publication))
            {
                current = new SessionEpochSnapshot(_value, _publication);
                return false;
            }

            _publication = new object();
            current = new SessionEpochSnapshot(_value, _publication);
            return true;
        }
    }
}

public readonly record struct SessionEpochSnapshot(long Value, object Publication);
