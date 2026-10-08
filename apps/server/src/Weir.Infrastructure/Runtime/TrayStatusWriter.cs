using System.Text;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.Settings;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// Keeps <see cref="TrayStatus.FileName"/> in Weir's data folder true to what the tray shows: written once the server starts,
/// again whenever the pause, the files waiting on a person or the media managers that do not answer change, and a last time
/// when the server stops cleanly, saying it is no longer running. A burst of changes is read once, at most once a
/// <see cref="Settle"/>, and the file is replaced (whole, then renamed into place) only when its contents differ.
/// </summary>
public sealed class TrayStatusWriter : BackgroundService
{
    /// <summary>The least time between one reading and the next, so a busy queue does not rewrite the file for every job.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    /// <summary>The data that makes up the status: the pause, the files that wait on a person (their jobs and scans move them), and the managers.</summary>
    private static readonly HashSet<string> Topics = [DataTopics.Pause, DataTopics.Jobs, DataTopics.LibraryScan, DataTopics.Libraries, DataTopics.Connections];

    private readonly WeirOptions _options;
    private readonly SqliteDatabase _database;
    private readonly SuiteSettingsStore _settings;
    private readonly FileStateStore _files;
    private readonly MediaManagerConnectionStore _managers;
    private readonly DataChangePublisher _changes;
    private readonly TimeProvider _time;
    private readonly ILogger<TrayStatusWriter> _logger;
    private readonly Channel<bool> _stale = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private BroadcastSubscription<string>? _heard;
    private TrayStatus? _status;
    private string _written = string.Empty;

    public TrayStatusWriter(
        WeirOptions options,
        SqliteDatabase database,
        SuiteSettingsStore settings,
        FileStateStore files,
        MediaManagerConnectionStore managers,
        DataChangePublisher changes,
        TimeProvider time,
        ILogger<TrayStatusWriter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _managers = managers ?? throw new ArgumentNullException(nameof(managers));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Listens before the server starts answering, so a change made from then on is never missed.</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _heard = _changes.Subscribe();
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Once the last reading is done, says in the file that the server has stopped.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_status is { } last)
        {
            Write(last with { ServerOk = false });
        }
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
            await Task.WhenAll(ListenAsync(stoppingToken), WriteAsync(stoppingToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server is stopping.
        }
    }

    /// <summary>Marks the status stale whenever data it is made of changes; the reading itself is left to the loop that throttles it.</summary>
    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await foreach (var topic in _heard!.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (Topics.Contains(topic))
            {
                _stale.Writer.TryWrite(true);
            }
        }
    }

    private async Task WriteAsync(CancellationToken stoppingToken)
    {
        await ReadAndWriteAsync(stoppingToken).ConfigureAwait(false);
        await foreach (var _ in _stale.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await Task.Delay(Settle, stoppingToken).ConfigureAwait(false);
            await ReadAndWriteAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReadAndWriteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var status = await ReadAsync(stoppingToken).ConfigureAwait(false);
            _status = status;
            Write(status);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Weir could not read what the tray shows about it; the tray may be a step behind until the next change.");
        }
    }

    private async Task<TrayStatus> ReadAsync(CancellationToken stoppingToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, stoppingToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var row = await _settings.GetAsync(uow).ConfigureAwait(false);
            var pause = row is null ? null : PauseState.Resolve(row, Timestamp.UtcNow(_time).AsUtc);
            var files = await _files.CountWaitingOnPersonAsync(uow).ConfigureAwait(false);
            var managers = await _managers.ListEnabledAsync(uow).ConfigureAwait(false);
            return new TrayStatus(
                pause is { Paused: true },
                pause is { Paused: true, PausedUntil: { } until } ? until.AsUtc : null,
                files,
                [.. managers.Where(manager => manager.LastTestOk == false).Select(manager => manager.Label)],
                ServerOk: true);
        }
    }

    private void Write(TrayStatus status)
    {
        var contents = status.ToJson();
        if (contents == _written)
        {
            return;
        }

        try
        {
            AtomicFileWriter.Replace(_options.WeirHome, TrayStatus.FileName, Encoding.UTF8.GetBytes(contents));
            _written = contents;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Weir could not tell the tray how it is; the tray may be a step behind until the next change.");
        }
    }
}
