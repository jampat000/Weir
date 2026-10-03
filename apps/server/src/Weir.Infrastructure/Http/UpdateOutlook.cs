using Weir.Core.Json;
using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Http;

/// <summary>Whether a newer Weir exists, as far as Weir last found out.</summary>
/// <param name="Status">
/// <c>checking</c> (nothing learned yet), <c>up_to_date</c>, <c>update_available</c>, <c>downloaded</c> (waiting for the tray to
/// install it), <c>not_published</c> or <c>unavailable</c>.
/// </param>
/// <param name="LatestVersion">The newest version known, or the one waiting to install; null when none is known.</param>
public sealed record UpdateOutlookSnapshot(string Status, string? LatestVersion);

/// <summary>
/// What Weir last learned about newer releases, kept so System can show it on every read without asking GitHub each time. A
/// read that finds the answer stale starts a fresh check in the background and still answers at once.
/// </summary>
public sealed class UpdateOutlook
{
    public const string Checking = "checking";
    public const string Downloaded = "downloaded";

    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    private readonly UpdateStatusReader _status;
    private readonly UpdateFiles _files;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private UpdateOutlookSnapshot _known = new(Checking, null);
    private DateTimeOffset? _checkedAt;
    private bool _checkFailed;
    private bool _checking;

    public UpdateOutlook(UpdateStatusReader status, UpdateFiles files, TimeProvider time)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The outlook now: an update the tray already downloaded wins over what the last check found.</summary>
    public UpdateOutlookSnapshot Current()
    {
        StartCheckWhenStale();
        var state = _files.ReadState();
        if (state.Get("downloaded")?.IsTruthy == true)
        {
            return new UpdateOutlookSnapshot(Downloaded, TextOf(state.Get("pending_version")));
        }

        lock (_gate)
        {
            return _known;
        }
    }

    /// <summary>Asks for the latest release and keeps the answer.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var status = await _status.ReadAsync(cancellationToken).ConfigureAwait(false);
        var outlook = new UpdateOutlookSnapshot(TextOf(status.Get("status")) ?? Checking, TextOf(status.Get("latest_version")));
        lock (_gate)
        {
            _known = outlook;
            _checkedAt = _time.GetUtcNow();
            _checkFailed = outlook.Status is "unavailable";
        }
    }

    private void StartCheckWhenStale()
    {
        lock (_gate)
        {
            var wait = _checkFailed ? RetryAfterFailure : FreshFor;
            if (_checking || (_checkedAt is { } at && _time.GetUtcNow() - at < wait))
            {
                return;
            }

            _checking = true;
        }

        _ = Task.Run(RunCheckAsync, CancellationToken.None);
    }

    private async Task RunCheckAsync()
    {
        try
        {
            await CheckAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _checking = false;
            }
        }
    }

    private static string? TextOf(WireValue? value) => value is WireString text && text.Value.Length > 0 ? text.Value : null;
}
