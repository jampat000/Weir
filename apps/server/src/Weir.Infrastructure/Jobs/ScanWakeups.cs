using System.Collections.Concurrent;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// When Weir next looks at each library's watched folder: its next periodic scan, and any look booked for the moment a
/// file there stops being held. Processing counts an arriving file down to the real next look instead of guessing.
/// </summary>
/// <remarks>
/// A scan that holds a file until a known time — it is still being written, it changed too recently, the library's
/// window reopens later — books the library's next look for then, and the scan timer honours the booking on its next
/// tick. Before this a held file waited for the library's next periodic scan, five minutes by default, after its hold had
/// already ended: Processing showed it "due now" in Arriving while a lane stood free. Only the earliest booking per
/// library is kept, because the scan that runs then books the next one itself.
/// </remarks>
public sealed class ScanWakeups
{
    private readonly ConcurrentDictionary<long, DateTimeOffset> _at = new();
    private readonly ConcurrentDictionary<long, DateTimeOffset> _periodic = new();

    /// <summary>The scan timer's next periodic look at <paramref name="libraryId"/>.</summary>
    public void RecordNextPeriodic(long libraryId, DateTimeOffset at) => _periodic[libraryId] = at;

    /// <summary>The scan timer stops looking at <paramref name="libraryId"/> on a schedule (switched off or gone).</summary>
    public void ForgetPeriodic(long libraryId) => _periodic.TryRemove(libraryId, out _);

    /// <summary>The next look at <paramref name="libraryId"/>: the earlier of a booked look and the next periodic one.</summary>
    public DateTimeOffset? NextLookFor(long libraryId)
    {
        DateTimeOffset? booked = _at.TryGetValue(libraryId, out var at) ? at : null;
        DateTimeOffset? periodic = _periodic.TryGetValue(libraryId, out var next) ? next : null;
        return booked is null ? periodic : periodic is null ? booked : booked < periodic ? booked : periodic;
    }

    /// <summary>Book a look at <paramref name="libraryId"/> at <paramref name="at"/>, unless an earlier one is booked.</summary>
    public void Request(long libraryId, DateTimeOffset at) =>
        _at.AddOrUpdate(libraryId, at, (_, booked) => at < booked ? at : booked);

    /// <summary>Whether a look is due for <paramref name="libraryId"/> now; a due booking is used up.</summary>
    public bool TakeDue(long libraryId, DateTimeOffset now)
    {
        if (_at.TryGetValue(libraryId, out var at) && at <= now)
        {
            _at.TryRemove(KeyValuePair.Create(libraryId, at));
            return true;
        }

        return false;
    }

    /// <summary>The booked look for <paramref name="libraryId"/>, if any.</summary>
    public DateTimeOffset? BookedFor(long libraryId) => _at.TryGetValue(libraryId, out var at) ? at : null;
}
