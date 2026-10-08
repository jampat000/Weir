using System.Text;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// Hears the tray ask to pause or resume processing. The tray writes <see cref="FileName"/> in Weir's data folder, holding
/// <c>{ "paused": true, "requested_at": "2026-10-09T08:30:00Z" }</c> (<c>false</c> to resume); the request is applied through
/// <see cref="SuitePauseService"/> as "the tray", which records it in Activity, and the file is deleted. A pause asked for from
/// the tray lasts until it is resumed and leaves the choice to keep looking for new files as it stands. A file that is not that
/// shape is ignored and deleted. Only a process that can write to the data folder can make a request; no route takes one.
/// </summary>
public sealed class TrayPauseRequestWatcher : BackgroundService
{
    public const string FileName = "pause-request.json";

    /// <summary>Who Activity says paused or resumed processing.</summary>
    public const string RequestedBy = "the tray";

    private readonly WeirOptions _options;
    private readonly SqliteDatabase _database;
    private readonly SuitePauseService _pause;
    private readonly TimeProvider _time;
    private readonly ILogger<TrayPauseRequestWatcher> _logger;
    private readonly Channel<bool> _written = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private FileSystemWatcher? _watcher;

    public TrayPauseRequestWatcher(WeirOptions options, SqliteDatabase database, SuitePauseService pause, TimeProvider time, ILogger<TrayPauseRequestWatcher> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _pause = pause ?? throw new ArgumentNullException(nameof(pause));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts listening before the server starts answering, so a request made from then on is never missed.</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher = TryWatch();
        return base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            // A request left by a tray that asked while the server was down is answered first.
            await ApplyAsync(stoppingToken).ConfigureAwait(false);
            await foreach (var _ in _written.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await Task.Delay(TrayHandOffWatcher.Settle, stoppingToken).ConfigureAwait(false);
                await ApplyAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server is stopping.
        }
    }

    private FileSystemWatcher? TryWatch()
    {
        try
        {
            var watcher = new FileSystemWatcher(_options.WeirHome, FileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += (_, _) => _written.Writer.TryWrite(true);
            watcher.Changed += (_, _) => _written.Writer.TryWrite(true);
            watcher.Renamed += (_, _) => _written.Writer.TryWrite(true);

            // Events lost to a full buffer could have been the request.
            watcher.Error += (_, _) => _written.Writer.TryWrite(true);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Weir could not watch {Folder} for the tray's pause requests; Pause and Resume in the tray will not work until Weir is restarted.", _options.WeirHome);
            return null;
        }
    }

    private async Task ApplyAsync(CancellationToken stoppingToken)
    {
        var path = Path.Join(_options.WeirHome, FileName);
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            text = await File.ReadAllTextAsync(path, Encoding.UTF8, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still being written; the write that finishes it is heard as well.
            _logger.LogDebug(exception, "The tray's pause request could not be read yet.");
            return;
        }

        if (Read(text) is { } paused)
        {
            try
            {
                await ChangeAsync(paused, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
            {
                _logger.LogWarning(exception, "Weir could not {Change} processing as the tray asked.", paused ? "pause" : "resume");
            }
        }
        else
        {
            _logger.LogInformation("Weir ignored a pause request from the tray that it could not read.");
        }

        Delete(path);
    }

    /// <summary>Whether the request pauses (true) or resumes (false); null for text that is not a request.</summary>
    private static bool? Read(string text)
    {
        try
        {
            return WireJsonParser.Parse(text) is WireObject request && request.Get("paused") is WireBool paused ? paused.Value : null;
        }
        catch (WireJsonDecodeException)
        {
            return null;
        }
    }

    private async Task ChangeAsync(bool paused, CancellationToken stoppingToken)
    {
        var now = Timestamp.UtcNow(_time);
        var uow = await UnitOfWork.OpenAsync(_database, stoppingToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var current = await _pause.CurrentAsync(uow, now).ConfigureAwait(false);
            await _pause.ChangeAsync(uow, paused, minutes: null, keepEnd: false, current.ScanWhilePaused, now, RequestedBy).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
    }

    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Weir could not remove the tray's pause request {Path}.", path);
        }
    }
}
