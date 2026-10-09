using System.Text.Json;

namespace Weir.Tray;

/// <summary>
/// The tray's way to have a copy of Weir's data saved before it applies an update. Every path that applies one asks first
/// (<see cref="TrayShutdown"/>), and none applies it when the copy could not be saved.
/// </summary>
interface IUpdateBackup
{
    /// <summary>
    /// Has the running server save a copy of Weir's data before the update to <paramref name="targetVersion"/> is applied. True
    /// when the update may go ahead: the copy is saved, or the server is too old to make one and the server that starts after the
    /// update will. False when the copy could not be saved: the person has been told why, and the update must not be applied.
    /// </summary>
    Task<bool> SaveBeforeApplyAsync(string? targetVersion);
}

/// <summary>How the running server answered a request for a copy of Weir's data.</summary>
enum UpdateBackupOutcome
{
    /// <summary>The copy is saved.</summary>
    Saved,

    /// <summary>The copy could not be saved; <see cref="UpdateBackupAnswer.Reason"/> says why.</summary>
    Failed,

    /// <summary>The server never answered: a build from before it could (or none is running). The server that starts after the update saves the copy.</summary>
    NotAnswered,
}

/// <param name="Outcome">How the request ended.</param>
/// <param name="Reason">Why the copy could not be saved, in the server's plain words, when <paramref name="Outcome"/> is Failed.</param>
readonly record struct UpdateBackupAnswer(UpdateBackupOutcome Outcome, string? Reason);

/// <summary>
/// Asks the running server for a copy of Weir's data through two files in the runtime home, as Pause does
/// (<c>PauseRequestFile</c>): <c>update-backup-request.json</c> is the tray's request and <c>update-backup-result.json</c> the
/// server's answer, first <c>started</c> (it heard, and is saving), then <c>saved</c> or <c>failed</c> with a plain reason. Files,
/// not a route, because the tray has no signed-in session, nothing is added to the server's public surface, and only a process
/// that can write the runtime home (readable by the account running Weir alone) can ask. A server that never writes
/// <c>started</c> does not know the request, and that is not a failure.
/// </summary>
sealed class UpdateBackupRequest(string runtimeHome, TimeProvider clock, Func<bool> serverIsRunning)
{
    internal const string RequestFileName = "update-backup-request.json";
    internal const string ResultFileName = "update-backup-result.json";

    private const string StartedState = "started";
    private const string SavedState = "saved";
    private const string FailedState = "failed";

    /// <summary>How long the server has to say it heard the request. It answers within a second when it can.</summary>
    internal static readonly TimeSpan HearingTime = TimeSpan.FromSeconds(15);

    /// <summary>The longest a copy may take once the server has said it is saving.</summary>
    internal static readonly TimeSpan LongestSave = TimeSpan.FromMinutes(30);

    /// <summary>How often the answer is read.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    internal async Task<UpdateBackupAnswer> AskAsync(string targetVersion, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("n");
        var asked = clock.GetTimestamp();
        Remove(ResultFileName);
        AtomicFile.WriteAllText(
            runtimeHome,
            RequestFileName,
            JsonSerializer.Serialize(new { id, requested_at = clock.GetUtcNow(), target_version = targetVersion }, Json));

        var heard = false;
        while (true)
        {
            var result = Read(id);
            switch (result?.State)
            {
                case SavedState:
                    return new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null);
                case FailedState:
                    return new UpdateBackupAnswer(UpdateBackupOutcome.Failed, result.Reason);
                case StartedState:
                    heard = true;
                    break;
                default:
                    break;
            }

            var waited = clock.GetElapsedTime(asked);
            if (!heard && waited >= HearingTime)
            {
                // Taken back, so a server that wakes up later does not save a copy for an update the tray went ahead with.
                Remove(RequestFileName);
                return new UpdateBackupAnswer(UpdateBackupOutcome.NotAnswered, null);
            }
            if (heard && !serverIsRunning())
            {
                return Failed("Weir stopped while it was saving the copy. Start Weir again, then try again.");
            }
            if (heard && waited >= LongestSave)
            {
                return Failed("Saving the copy is taking too long. Check that the disk Weir keeps its data on is working, then try again.");
            }
            await Task.Delay(PollInterval, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private static UpdateBackupAnswer Failed(string reason) => new(UpdateBackupOutcome.Failed, reason);

    private sealed record Wire(string? Id, string? State, string? Reason);

    private Wire? Read(string id)
    {
        try
        {
            using var stream = new FileStream(
                Path.Combine(runtimeHome, ResultFileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var wire = JsonSerializer.Deserialize<Wire>(stream, Json);
            return wire?.Id == id ? wire : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Being replaced, or not finished: read again on the next look.
            return null;
        }
    }

    private void Remove(string fileName)
    {
        try
        {
            File.Delete(Path.Combine(runtimeHome, fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not remove {fileName}: {ex.Message}");
        }
    }
}

/// <summary>
/// The hook the apply paths call: asks the running server for the copy, and when it cannot be saved says so (the log, a message
/// for the person, and a record that keeps the next start from applying the update by itself). It decides nothing else.
/// </summary>
/// <param name="ask">Asks the running server (<see cref="UpdateBackupRequest.AskAsync"/>).</param>
/// <param name="runtimeHome">Where the record of a failed copy is kept.</param>
/// <param name="serverIsRunning">Whether there is a server to ask. With none, the server that starts after the update saves the copy.</param>
/// <param name="onNotUpdated">Told why the update will not be applied, so the person is told.</param>
sealed class UpdateBackupHook(
    Func<string, CancellationToken, Task<UpdateBackupAnswer>> ask,
    string runtimeHome,
    Func<bool> serverIsRunning,
    Action<string> onNotUpdated) : IUpdateBackup
{
    private const string FailedMarkerFileName = "update-backup-failed";

    /// <summary>
    /// <c>update-not-applied.json</c> in the runtime home: <c>{ "version": …, "reason": … }</c> while an update is held back for
    /// want of a copy. The server reads it into System › About (<c>UpdateFiles.NotAppliedFileName</c>); a file of its own so it
    /// does not depend on how update-state.json is written.
    /// </summary>
    private const string NotAppliedFileName = "update-not-applied.json";

    private static readonly JsonSerializerOptions NotAppliedJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<bool> SaveBeforeApplyAsync(string? targetVersion)
    {
        if (!serverIsRunning())
        {
            TrayLog.Write("No server is running to save a copy of Weir's data first; the server that starts after the update will.");
            return true;
        }

        var version = string.IsNullOrWhiteSpace(targetVersion) ? "unknown" : targetVersion;
        TrayLog.Write($"Asking the server to save a copy of Weir's data before updating to v{version}.");
        UpdateBackupAnswer answer;
        try
        {
            answer = await ask(version, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not ask the server to save a copy of Weir's data: {ex.Message}");
            answer = new UpdateBackupAnswer(
                UpdateBackupOutcome.Failed,
                "Weir couldn't ask itself to save the copy because its data folder can't be written to. Check that the account Weir runs as can write to it, then try again.");
        }

        switch (answer.Outcome)
        {
            case UpdateBackupOutcome.Saved:
                TrayLog.Write("The server saved a copy of Weir's data.");
                Forget();
                return true;
            case UpdateBackupOutcome.NotAnswered:
                TrayLog.Write("The server did not answer the request for a copy of Weir's data, so it is an older build; the server that starts after the update will save it.");
                return true;
            default:
                var reason = answer.Reason ?? "Something unexpected went wrong. The details are in Weir's log files.";
                TrayLog.Write($"The update to v{version} was not applied: the server could not save a copy of Weir's data. {reason}");
                Remember(version, reason);
                onNotUpdated(reason);
                return false;
        }
    }

    /// <summary>
    /// Whether an earlier run could not save a copy before applying <paramref name="version"/>. The start-up install, which has no
    /// running server to ask, leaves such an update alone: the server it started would only fail to save the copy again.
    /// </summary>
    internal static bool FailedFor(string runtimeHome, string version)
    {
        try
        {
            return File.ReadAllText(Path.Combine(runtimeHome, FailedMarkerFileName)).Trim() == version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the reason the last update was held back. A tray that starts has applied nothing yet, so a reason from the run
    /// before it is no longer the case; the record that keeps the start-up install away stays until a copy is saved.
    /// </summary>
    internal static void ForgetReason(string runtimeHome) => Delete(runtimeHome, NotAppliedFileName);

    private void Remember(string version, string reason)
    {
        try
        {
            AtomicFile.WriteAllText(runtimeHome, FailedMarkerFileName, version);
            AtomicFile.WriteAllText(
                runtimeHome, NotAppliedFileName, JsonSerializer.Serialize(new { version, reason }, NotAppliedJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not record that the copy for v{version} failed: {ex.Message}");
        }
    }

    private void Forget()
    {
        Delete(runtimeHome, FailedMarkerFileName);
        Delete(runtimeHome, NotAppliedFileName);
    }

    private static void Delete(string runtimeHome, string fileName)
    {
        try
        {
            File.Delete(Path.Combine(runtimeHome, fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not remove {fileName}: {ex.Message}");
        }
    }
}
