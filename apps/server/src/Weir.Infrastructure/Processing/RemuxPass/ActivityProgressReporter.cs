using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// <c>ProcessingActivityProgressReporter</c>: one live <c>processing.file_processing_progress</c> row per pass, inserted on the
/// first save and rewritten after. A failed write never interrupts ffprobe or ffmpeg.
/// </summary>
/// <remarks>
/// Reports arrive on the tool's output reader, twice a second from ffmpeg. <see cref="Report"/> only hands the report
/// over: a background writer saves the newest one at most once every <see cref="MinimumInterval"/>, so reading the
/// tool's output never waits on the database and a pass takes the write lock a quarter as often (#710). A report whose
/// status differs from the last one saved (started, finishing, finished, failed) is saved without waiting.
/// </remarks>
public sealed class ActivityProgressReporter
{
    /// <summary>The shortest time between two saves of the same status.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);

    private readonly SqliteDatabase? _database;
    private readonly Func<PyDict, Task> _save;
    private readonly long _jobId;
    private readonly PyDict _extra;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private PyDict? _pending;
    private string? _savedStatus;
    private long? _lastSavedAt;
    private bool _writing;
    private bool _completed;
    private TaskCompletionSource _saveNow = NewSignal();
    private Task _writer = Task.CompletedTask;

    public ActivityProgressReporter(SqliteDatabase database, long jobId, PyDict extra, ILogger logger, TimeProvider time)
        : this(
            database ?? throw new ArgumentNullException(nameof(database)),
            logger ?? throw new ArgumentNullException(nameof(logger)),
            save: null,
            jobId,
            extra,
            time)
    {
    }

    /// <summary>For tests: every save goes to <paramref name="save"/> instead of the database.</summary>
    internal ActivityProgressReporter(Func<PyDict, Task> save, long jobId, PyDict extra, TimeProvider time)
        : this(database: null, logger: null, save ?? throw new ArgumentNullException(nameof(save)), jobId, extra, time)
    {
    }

    private ActivityProgressReporter(SqliteDatabase? database, ILogger? logger, Func<PyDict, Task>? save, long jobId, PyDict extra, TimeProvider time)
    {
        _database = database;
        _logger = logger;
        _save = save ?? SaveToActivityAsync;
        _jobId = jobId;
        _extra = extra ?? new PyDict();
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The progress row, once written; the handler turns it into the completed row.</summary>
    public long? ActivityId { get; private set; }

    /// <summary>Takes a report without waiting for it to be saved. Reports after <see cref="CompleteAsync"/> are ignored.</summary>
    public void Report(PyDict payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var body = new PyDict().Set("job_id", _jobId);
        foreach (var (key, value) in _extra.Items.Concat(payload.Items))
        {
            body.Set(key, value);
        }

        // The row is rewritten in place, so its created_at stays at the pass's start. This is how a reader
        // tells a long pass that is still reporting from one that died (LiveProgressStore).
        body.Set("reported_at", PyDateTime.UtcNow(_time).PydanticJson());

        lock (_lock)
        {
            if (_completed)
            {
                return;
            }

            _pending = body;
            if (StatusOf(body) != _savedStatus)
            {
                _saveNow.TrySetResult();
            }

            if (!_writing)
            {
                _writing = true;
                _writer = Task.Run(WriteAsync);
            }
        }
    }

    /// <summary>Saves the newest report now, without waiting out the interval, and returns once it is saved.</summary>
    public Task FlushAsync()
    {
        lock (_lock)
        {
            _saveNow.TrySetResult();
            return _writer;
        }
    }

    /// <summary>
    /// Saves the newest report and stops taking more. The handler calls this before it turns the row into the
    /// completed row, so no late progress save can overwrite that.
    /// </summary>
    public Task CompleteAsync()
    {
        lock (_lock)
        {
            _completed = true;
        }

        return FlushAsync();
    }

    /// <summary>One writer at a time: saves the pending report, waiting out the interval first, until none is pending.</summary>
    private async Task WriteAsync()
    {
        while (true)
        {
            lock (_lock)
            {
                if (_pending is null)
                {
                    _writing = false;
                    return;
                }
            }

            await WaitForTurnAsync().ConfigureAwait(false);
            PyDict body;
            lock (_lock)
            {
                body = _pending!;
                _pending = null;
                _savedStatus = StatusOf(body);
                if (_saveNow.Task.IsCompleted)
                {
                    _saveNow = NewSignal();
                }
            }

            await _save(body).ConfigureAwait(false);
            _lastSavedAt = _time.GetTimestamp();
        }
    }

    private async Task WaitForTurnAsync()
    {
        if (_lastSavedAt is not { } last)
        {
            return;
        }

        var wait = MinimumInterval - _time.GetElapsedTime(last);
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        Task saveNow;
        lock (_lock)
        {
            saveNow = _saveNow.Task;
        }

        using var interval = new CancellationTokenSource();
        await Task.WhenAny(saveNow, Task.Delay(wait, _time, interval.Token)).ConfigureAwait(false);
        await interval.CancelAsync().ConfigureAwait(false);
    }

    private async Task SaveToActivityAsync(PyDict body)
    {
        try
        {
            var uow = await UnitOfWork.OpenAsync(_database!).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                var name = FileName(body.Get("relative_media_path"));
                var detail = PyStrings.Slice(PyJsonWriter.Dumps(body, PyJsonFormat.Compact), 6000);
                if (ActivityId is { } id)
                {
                    await SqliteActivityWriter.UpdateAsync(uow, id, title: Title(StatusOf(body), name), detail: detail).ConfigureAwait(false);
                    await uow.CommitAsync().ConfigureAwait(false);
                    return;
                }

                var inserted = await SqliteActivityWriter.RecordAsync(
                    uow,
                    new ActivityEventDraft(ActivityEventTypes.ProcessingFileProcessingProgress, "processing", $"Processing {name}", detail)).ConfigureAwait(false);
                await uow.CommitAsync().ConfigureAwait(false);
                ActivityId = inserted;
            }
        }
#pragma warning disable CA1031 // A progress update must never interrupt the media pass; the next report is saved instead.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger!.LogWarning(exception, "Weir could not save a progress update; continuing the media pass.");
        }
    }

    private static string StatusOf(PyDict body) =>
        body.Get("status") is { IsTruthy: true } status ? PyConvert.Str(status) : "processing";

    private static string Title(string status, string name) => status switch
    {
        "waiting" => $"Waiting to process {name}",
        "finishing" => $"Finishing {name}",
        "finished" => $"{name} finished processing",
        "failed" => $"{name} could not be processed",
        _ => $"Processing {name}",
    };

    /// <summary><c>Path(str(relative_media_path or "")).name or "this file"</c>.</summary>
    private static string FileName(PyJson? value)
    {
        var text = value is { IsTruthy: true } ? PyConvert.Str(value) : string.Empty;
        var name = MediaPathNames.Name(text, OperatingSystem.IsWindows());
        return name.Length > 0 ? name : "this file";
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
