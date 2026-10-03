using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Runtime;

/// <summary>When the server started, each time it did, so System can say how often Weir restarted.</summary>
public sealed class ServerStartStore
{
    /// <summary>How long a start is remembered; far longer than any window System asks about.</summary>
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(365);

    /// <summary>Notes that the server started at <paramref name="at"/>, and forgets starts older than a year.</summary>
    public async Task RecordStartAsync(UnitOfWork uow, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(uow);
        await uow.ExecuteAsync(
            "INSERT INTO server_starts (started_at) VALUES (@at)",
            ("@at", Stored(at))).ConfigureAwait(false);
        await uow.ExecuteAsync(
            "DELETE FROM server_starts WHERE started_at < @cutoff",
            ("@cutoff", Stored(at - KeepFor))).ConfigureAwait(false);
    }

    /// <summary>
    /// How many times the server was restarted since <paramref name="since"/>: the starts in that time that followed an earlier
    /// start. The first start of an install is not a restart.
    /// </summary>
    public Task<long> RestartsSinceAsync(UnitOfWork uow, DateTimeOffset since)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.CountAsync(
            "SELECT COUNT(*) FROM server_starts WHERE started_at >= @since AND id > (SELECT MIN(id) FROM server_starts)",
            ("@since", Stored(since)));
    }

    private static string Stored(DateTimeOffset moment) => Timestamp.FromUtc(moment.UtcDateTime).ToSqlite();
}
