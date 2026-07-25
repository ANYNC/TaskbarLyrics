namespace TaskbarLyrics.App;

internal sealed class ActiveSessionCache<TSession>
    where TSession : class
{
    private TSession? _session;

    public void Remember(TSession? session)
    {
        _session = session;
    }

    public TSession? FindIn(IEnumerable<TSession>? availableSessions)
    {
        if (_session is null || availableSessions is null)
        {
            return null;
        }

        foreach (var candidate in availableSessions)
        {
            if (ReferenceEquals(candidate, _session))
            {
                return _session;
            }
        }

        _session = null;
        return null;
    }

    public void Clear()
    {
        _session = null;
    }
}
