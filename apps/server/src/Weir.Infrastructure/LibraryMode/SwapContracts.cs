using Weir.Core.LibraryMode;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>Whether a written cleaned copy may replace the original (#500's full output check plugs in here).</summary>
public interface ISwapOutputValidator
{
    /// <param name="originalPath">The file being replaced.</param>
    /// <param name="outputPath">The cleaned copy.</param>
    /// <param name="originalDurationSeconds">The original's probed duration, when the caller knows it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<SwapValidation> ValidateAsync(string originalPath, string outputPath, double? originalDurationSeconds, CancellationToken cancellationToken);
}

/// <summary>An output check's answer.</summary>
public sealed record SwapValidation(bool Passed, string? Problem)
{
    public static SwapValidation Pass { get; } = new(true, null);

    public static SwapValidation Fail(string problem) => new(false, problem);
}

/// <summary>Writes the cleaned copy of the original to <paramref name="tempPath"/> (ffmpeg, for the remux pass).</summary>
public delegate Task SwapOutputWriter(string tempPath, CancellationToken cancellationToken);

/// <summary>
/// #735: keep the pre-clean original instead of deleting it once the swap commits. Present only while the library's
/// "keep the original after clean" setting is on.
/// </summary>
/// <param name="LibraryFolders">The library's #505 folders, to find which one holds the file being swapped — it decides
/// the default originals folder and the file's kept relative path (<see cref="Weir.Core.LibraryMode.OriginalsPathPlanner"/>).</param>
/// <param name="OriginalsFolder">The library's explicit originals folder, or blank for the default.</param>
public sealed record KeepOriginalOptions(IReadOnlyList<string> LibraryFolders, string? OriginalsFolder);

/// <summary>Per-library choices that change preflight, and what the caller already knows about the original.</summary>
/// <param name="AllowHardlinked"><c>clean_hardlinked_files</c> (#508): replace a file that has other hard links anyway.</param>
/// <param name="OriginalDurationSeconds">
/// The original's duration from the caller's own probe, which the output check compares the copy against, so the
/// original is not probed a second time (#716).
/// </param>
/// <param name="KeepOriginal">#735: non-null moves the backup into an originals folder instead of deleting it.</param>
public sealed record SwapOptions(bool AllowHardlinked = false, double? OriginalDurationSeconds = null, KeepOriginalOptions? KeepOriginal = null)
{
    public static SwapOptions Default { get; } = new();
}

/// <summary>What preflight found. <see cref="Refusal"/> is null when the swap may go ahead.</summary>
public sealed record SwapPreflight(
    string OriginalPath,
    SwapOutcome? Refusal,
    string? Message,
    SourceFingerprint Fingerprint,
    IReadOnlyList<string> Notes)
{
    public bool Ready => Refusal is null;

    public string TempPath => SafeSwapRules.TempPath(OriginalPath);

    public string BackupPath => SafeSwapRules.BackupPath(OriginalPath);
}

/// <summary>
/// How a swap ended, in words for the operator, plus anything worth a warning. After a commit, <c>BackupRemoved</c> is false
/// when the backup could not be deleted or moved (the startup sweep retries it). <c>KeptOriginalPath</c> is set only once
/// the original has actually been moved there (#735); it stays null while the setting is off, or while the move itself
/// is still pending the sweep.
/// </summary>
public sealed record SwapResult(SwapOutcome Outcome, string Message, bool BackupRemoved, IReadOnlyList<string> Warnings, string? KeptOriginalPath = null)
{
    public bool Committed => Outcome == SwapOutcome.Committed;
}
