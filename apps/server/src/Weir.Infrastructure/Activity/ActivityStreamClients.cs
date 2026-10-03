namespace Weir.Infrastructure.Activity;

/// <summary>
/// How many browsers hold the live Activity stream open right now. Whatever is sampled only for the sake of a screen
/// (the System traces) can slow down when nobody is watching, and System shows the count as "Browsers live".
/// </summary>
public sealed class ActivityStreamClients
{
    private int _open;

    /// <summary>The streams open now.</summary>
    public int Count => Volatile.Read(ref _open);

    /// <summary>Counts one stream as open until the returned lease is disposed.</summary>
    public IDisposable Open()
    {
        Interlocked.Increment(ref _open);
        return new Lease(this);
    }

    private sealed class Lease(ActivityStreamClients owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref owner._open);
            }
        }
    }
}
