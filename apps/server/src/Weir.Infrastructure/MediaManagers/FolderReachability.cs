using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Knows which of the switched-on workflows' own folders (watched, work and output) Weir cannot reach, for the tray's dot. It
/// looks every <see cref="Every"/> and at once when the workflows change, whether or not a browser is open, and so asks only
/// about the folders themselves (<see cref="LibraryFolderChainCheck.UnreachableFolders"/>): the media managers and download
/// clients stay with <see cref="FolderChecksTask"/>, which looks only while a screen is watching. When the answer is not the one
/// it gave last time it says so on <see cref="DataTopics.FolderChecks"/>, because it is the one that knows.
/// </summary>
public sealed class FolderReachability : BackgroundService
{
    /// <summary>How often the folders are looked at when nothing else prompts it.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    private readonly SqliteDatabase _database;
    private readonly LibraryStore _libraries;
    private readonly WeirOptions _options;
    private readonly IFolderProbe _probe;
    private readonly DataChangePublisher _changes;
    private readonly TimeProvider _time;
    private readonly ILogger<FolderReachability> _logger;
    private readonly Channel<bool> _stale = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly TaskCompletionSource _firstLook = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BroadcastSubscription<string>? _heard;
    private IReadOnlyList<string> _unreachable = [];

    public FolderReachability(
        SqliteDatabase database,
        LibraryStore libraries,
        WeirOptions options,
        IFolderProbe probe,
        DataChangePublisher changes,
        TimeProvider time,
        ILogger<FolderReachability> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The folders Weir cannot reach, each in plain words ("The watched folder for Movies"), in workflow order.</summary>
    public IReadOnlyList<string> Unreachable => Volatile.Read(ref _unreachable);

    /// <summary>Completes once the folders have been looked at for the first time, so nothing says "all well" before they have been.</summary>
    public Task FirstLook => _firstLook.Task;

    /// <summary>Listens before the server starts answering, so a change made from then on is never missed.</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _heard = _changes.Subscribe();
        return base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _heard?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(ListenAsync(stoppingToken), KeepLookingAsync(stoppingToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server is stopping.
        }
    }

    /// <summary>Looks at the folders now, and says so if the answer is not the one it gave last time. A look that fails keeps the last answer.</summary>
    internal async Task LookAsync(CancellationToken cancellationToken)
    {
        try
        {
            var unreachable = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!unreachable.SequenceEqual(Unreachable, StringComparer.Ordinal))
            {
                Volatile.Write(ref _unreachable, unreachable);
                _changes.Publish(DataTopics.FolderChecks);
            }
        }
#pragma warning disable CA1031 // Nothing a look does may stop the server; it is logged and tried again.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Weir could not check whether it can reach its workflows' folders; the tray keeps the last answer until the next look.");
        }
        finally
        {
            _firstLook.TrySetResult();
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await foreach (var topic in _heard!.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (topic == DataTopics.Libraries)
            {
                _stale.Writer.TryWrite(true);
            }
        }
    }

    private async Task KeepLookingAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await LookAsync(stoppingToken).ConfigureAwait(false);
            await UntilNextLookAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the interval to pass or the workflows to change, whichever comes first.</summary>
    private async Task UntilNextLookAsync(CancellationToken stoppingToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await Task.WhenAny(_stale.Reader.WaitToReadAsync(wait.Token).AsTask(), Task.Delay(Every, _time, wait.Token)).ConfigureAwait(false);
        await wait.CancelAsync().ConfigureAwait(false);
        _stale.Reader.TryRead(out _);
    }

    private async Task<List<string>> ReadAsync(CancellationToken cancellationToken)
    {
        List<ProcessingLibraryRecord> workflows;
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            workflows = await _libraries.ListAsync(uow, enabledOnly: true).ConfigureAwait(false);
        }

        // A share that has gone quiet can hold the thread for as long as the system waits on it.
        return await BlockingWork.RunAsync(() => NamesOfUnreachable(workflows)).ConfigureAwait(false);
    }

    private List<string> NamesOfUnreachable(List<ProcessingLibraryRecord> workflows) =>
        [.. workflows.SelectMany(workflow => LibraryFolderChainCheck
            .UnreachableFolders(workflow, _options.WeirHome, _probe)
            .Select(folder => $"The {Role(folder)} folder for {workflow.Name}"))];

    private static string Role(LocalFolder folder) => folder switch
    {
        LocalFolder.Watched => "watched",
        LocalFolder.Work => "work",
        LocalFolder.Output => "output",
        _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, null),
    };
}
