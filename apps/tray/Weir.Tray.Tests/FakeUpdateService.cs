namespace Weir.Tray.Tests;

/// <summary>
/// Stands in for Velopack. It finds and downloads a fixed update and records which apply calls were made, in order;
/// <see cref="OnApply"/> lets a test look at the world at the moment an install would start.
/// </summary>
internal sealed class FakeUpdateService : IUpdateService
{
    internal const string AppliedAndExited = "apply-and-exit";
    internal const string AppliedAndRestarted = "apply-and-restart";
    internal const string AppliedLeftWaiting = "apply-left-waiting";

    private readonly List<string> _applied = [];

    /// <summary>What an earlier run left waiting on disk, or null for nothing.</summary>
    public string? LeftWaitingVersion { get; init; }

    /// <summary>Whether a check finds <see cref="FoundVersion"/>.</summary>
    public bool FindsUpdate { get; init; } = true;

    public string FoundVersion { get; init; } = "9.9.9";

    public Action<string>? OnApply { get; set; }

    /// <summary>The apply calls made so far, by the constants above.</summary>
    public IReadOnlyList<string> Applied => _applied;

    public bool IsInstalled => true;

    public bool HasPendingUpdate { get; private set; }

    public bool IsDownloaded { get; private set; }

    public string? PendingVersion => HasPendingUpdate ? FoundVersion : null;

    /// <summary>Marks an update as downloaded, as if a download during this run had finished.</summary>
    public FakeUpdateService AlreadyDownloaded()
    {
        HasPendingUpdate = true;
        IsDownloaded = true;
        return this;
    }

    public Task<bool> CheckForUpdateAsync()
    {
        HasPendingUpdate = FindsUpdate;
        return Task.FromResult(FindsUpdate);
    }

    public Task<bool> DownloadUpdateAsync()
    {
        IsDownloaded = HasPendingUpdate;
        return Task.FromResult(HasPendingUpdate);
    }

    public string? FindUpdateLeftWaiting() => LeftWaitingVersion;

    public void ApplyAndExit() => Record(AppliedAndExited);

    public void ApplyAndRestart() => Record(AppliedAndRestarted);

    public void ApplyLeftWaitingUpdateAndRestart() => Record(AppliedLeftWaiting);

    private void Record(string call)
    {
        _applied.Add(call);
        OnApply?.Invoke(call);
    }
}
