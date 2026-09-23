using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// <c>processing-vanished-file-sweep</c>: every five minutes, forget the rows of files that have left each library's watched
/// folder (#645), whether or not that library's watched-folder scan runs.
/// </summary>
/// <remarks>
/// 3.2.4 (the Deluno soak, 23 Sep 2026): the rule ran only inside a scan, and a library fed by a media manager's hand-offs
/// often has its periodic scan off, so on the soak rig the rows 3.2.3 was meant to clear never met a scan and stayed listed.
/// This runs the same rule on its own clock. It only forgets; it never queues work, so it cannot pick up a stray file a scan
/// would have processed. A library whose watched folder cannot be read is skipped, so an unmounted share never empties the list.
/// </remarks>
public sealed class VanishedFileSweepTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly WeirOptions _options;
    private readonly TimeProvider _time;

    public VanishedFileSweepTask(SqliteDatabase database, WeirOptions options, TimeProvider time)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public string Name => "processing-vanished-file-sweep";

    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => PeriodicSchedule.FailureCooldown;

    public string FailureMessage => "The sweep for files that left their watched folder failed.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        foreach (var library in await LibraryStore.ListAsync(uow, enabledOnly: false).ConfigureAwait(false))
        {
            var (runtime, _) = WatchedFolderScanOps.ResolvePathRuntimeForLibrary(library, _options.WeirHome);
            if (runtime is null || !Directory.Exists(runtime.WatchedFolder))
            {
                continue;
            }

            await ProcessingWatchedFolderScanDispatchJobHandler.ForgetVanishedFilesAsync(
                uow, library.Id, runtime.WatchedFolder, ProcessingMediaScopes.Normalize(library.MediaType), now).ConfigureAwait(false);
        }

        await uow.CommitAsync().ConfigureAwait(false);
    }
}
