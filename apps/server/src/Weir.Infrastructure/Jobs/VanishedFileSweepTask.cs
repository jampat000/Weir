using Microsoft.Extensions.Logging;
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
/// A library fed by a media manager's hand-offs often has its periodic scan off, so a rule that ran only inside a scan would
/// never clear its rows. This runs the same rule on its own clock. It only forgets; it never queues work, so it cannot pick up a stray file a scan
/// would have processed. A library whose watched folder cannot be read is skipped, so an unmounted share never empties the list.
/// </remarks>
public sealed partial class VanishedFileSweepTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly WeirOptions _options;
    private readonly LibraryStore _libraries;
    private readonly TimeProvider _time;
    private readonly ILogger<VanishedFileSweepTask> _logger;

    public VanishedFileSweepTask(SqliteDatabase database, WeirOptions options, LibraryStore libraries, TimeProvider time, ILogger<VanishedFileSweepTask> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "processing-vanished-file-sweep";

    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => PeriodicSchedule.FailureCooldown;

    public string FailureMessage => "The sweep for files that left their watched folder failed.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        IReadOnlyList<ProcessingLibraryRecord> libraries;
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            libraries = await _libraries.ListAsync(uow, enabledOnly: false).ConfigureAwait(false);
        }

        foreach (var library in libraries)
        {
            var (runtime, _) = WatchedFolderScanOps.ResolvePathRuntimeForLibrary(library, _options.WeirHome);
            if (runtime is null || !Directory.Exists(runtime.WatchedFolder))
            {
                continue;
            }

            var forgotten = await VanishedFiles.ForgetAsync(
                _database, library.Id, runtime.WatchedFolder, ProcessingMediaScopes.Normalize(library.MediaType), now, "sweep", cancellationToken).ConfigureAwait(false);
            if (forgotten.Count > 0)
            {
                // In the server log as well as Activity, so it can be checked on a machine where nobody signs in.
                LogForgotten(forgotten.Count, library.Name, string.Join(", ", forgotten));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot {Count} file(s) that left the watched folder of library {Library}: {Paths}")]
    private partial void LogForgotten(int count, string library, string paths);
}
