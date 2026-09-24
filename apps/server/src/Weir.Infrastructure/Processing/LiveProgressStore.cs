using Weir.Core.Json;

namespace Weir.Infrastructure.Processing;

/// <summary>How far the pass currently working on a file has got.</summary>
/// <param name="Percent">How much of the file has been written, 0 to 100.</param>
/// <param name="Message">What the pass says it is doing, in its own words.</param>
/// <param name="EtaSeconds">The pass's own estimate of the time left.</param>
/// <param name="Status"><c>processing</c> while the file is written; <c>finishing</c> during the final checks and hand-back.</param>
/// <param name="Speed">ffmpeg's speed as it reports it, for example <c>148x</c>.</param>
/// <param name="ElapsedSeconds">How long the pass has been writing.</param>
/// <param name="RemovedAudio">The audio tracks this pass is taking out, as the plan describes each one.</param>
/// <param name="RemovedSubtitles">The subtitle tracks this pass is taking out.</param>
public sealed record LiveProgress(
    double? Percent,
    string? Message,
    double? EtaSeconds,
    string Status,
    string? Speed,
    double? ElapsedSeconds,
    IReadOnlyList<string> RemovedAudio,
    IReadOnlyList<string> RemovedSubtitles)
{
    /// <summary>Builds the live entry from one of <see cref="RemuxPass.ActivityProgressReporter"/>'s reports.</summary>
    public static LiveProgress FromReport(PyDict body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new LiveProgress(
            CoercePercent(body.TryGetValue("percent", out var p) ? p : null),
            Text(body, "message"),
            CoerceSeconds(body.TryGetValue("eta_seconds", out var e) ? e : null),
            StatusOf(body),
            Text(body, "speed"),
            CoerceSeconds(body.TryGetValue("elapsed_seconds", out var el) ? el : null),
            Strings(body, "removed_audio"),
            Strings(body, "removed_subtitles"));
    }

    /// <summary><c>str(body.get("status") or "processing")</c>.</summary>
    public static string StatusOf(PyDict body) =>
        body.TryGetValue("status", out var status) && status is PyStr text && text.Value.Trim().Length > 0
            ? text.Value.Trim().ToLowerInvariant()
            : "processing";

    /// <summary>A non-blank string field, trimmed, or <see langword="null"/>. Shared with <see cref="RemuxPass.ActivityProgressReporter"/>.</summary>
    internal static string? Text(PyDict payload, string key) =>
        payload.TryGetValue(key, out var value) && value is PyStr text && text.Value.Trim().Length > 0 ? text.Value.Trim() : null;

    private static List<string> Strings(PyDict payload, string key) =>
        payload.TryGetValue(key, out var value) && value is PyList list
            ? list.Items.OfType<PyStr>().Select(item => item.Value).Where(item => item.Trim().Length > 0).ToList()
            : [];

    private static double? CoercePercent(PyJson? value)
    {
        var number = Number(value);
        return number is null || double.IsNaN(number.Value) ? null : Math.Clamp(number.Value, 0.0, 100.0);
    }

    private static double? CoerceSeconds(PyJson? value)
    {
        var number = Number(value);
        return number is null || double.IsNaN(number.Value) || number.Value < 0 ? null : number.Value;
    }

    private static double? Number(PyJson? value) => value switch
    {
        PyInt i => (double)i.Value,
        PyFloat f => f.Value,
        PyBool b => b.Value ? 1.0 : 0.0,
        PyStr s when double.TryParse(s.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}

/// <summary>
/// The process-wide, in-memory live progress of every file currently being worked on (#750), keyed by
/// <c>relative_media_path</c>. Registered as a singleton in <c>WeirPlatformServices</c>, so one instance is shared by
/// every running pass and every open stream in the process; <see cref="RemuxPass.ActivityProgressReporter"/>
/// updates it on every progress line a pass reports, which never touches the database: the database only gets a row
/// at the start of a pass, when its stage changes, and at the end or on failure. A restart starts this store empty,
/// which is fine because a job that was mid-pass restarts anyway.
/// </summary>
public sealed class LiveProgressStore
{
    /// <summary>The only statuses a running pass is "live" under; anything else (waiting, finished, failed) is not shown here.</summary>
    private static readonly HashSet<string> LiveStatuses = new(StringComparer.Ordinal) { "processing", "finishing" };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LiveProgress> _byPath = new(StringComparer.Ordinal);
    private readonly HashSet<TaskCompletionSource<long>> _waiters = [];
    private long _version;

    /// <summary>Whether <paramref name="status"/> is one <see cref="Update"/> should keep, rather than <see cref="Remove"/>.</summary>
    public static bool IsLiveStatus(string status) => LiveStatuses.Contains(status);

    /// <summary>The version bumped on every <see cref="Update"/> or <see cref="Remove"/> that actually changed something.</summary>
    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <summary>Records the newest progress for <paramref name="relativeMediaPath"/> and wakes every waiter. Cheap: no I/O.</summary>
    public void Update(string relativeMediaPath, LiveProgress progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeMediaPath);
        ArgumentNullException.ThrowIfNull(progress);
        Notify(() => _byPath[relativeMediaPath] = progress);
    }

    /// <summary>Drops <paramref name="relativeMediaPath"/> from the live set: the pass on it finished, failed or is only waiting.</summary>
    public void Remove(string relativeMediaPath)
    {
        if (string.IsNullOrWhiteSpace(relativeMediaPath))
        {
            return;
        }

        Notify(() => _byPath.Remove(relativeMediaPath));
    }

    /// <summary>A snapshot of every file with live progress, safe to enumerate without holding the lock.</summary>
    public IReadOnlyDictionary<string, LiveProgress> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, LiveProgress>(_byPath, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The current state at once when the version already differs from <paramref name="previousVersion"/>, otherwise the
    /// next change, or <see langword="null"/> after <paramref name="timeout"/>. Every open stream calls this on the same
    /// store, so one update wakes them all and nothing polls the store on a timer.
    /// </summary>
    public async Task<long?> WaitForChangeAsync(long previousVersion, TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(time);
        var waiter = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_version != previousVersion)
            {
                return _version;
            }

            _waiters.Add(waiter);
        }

        try
        {
            return await waiter.Task.WaitAsync(timeout, time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            lock (_gate)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    private void Notify(Action mutate)
    {
        TaskCompletionSource<long>[] waiters;
        long version;
        lock (_gate)
        {
            mutate();
            version = ++_version;
            waiters = [.. _waiters];
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult(version);
        }
    }
}
