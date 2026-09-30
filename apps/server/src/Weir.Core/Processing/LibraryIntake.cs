namespace Weir.Core.Processing;

/// <summary>
/// The two intake settings a workflow holds on its own: how long a new file must stay unchanged before Weir starts on it,
/// and the smallest file it takes. Settings › Performance holds neither.
/// </summary>
public static class LibraryIntake
{
    /// <summary>What a new workflow waits for: a file is ready once it has not changed for this many seconds.</summary>
    public const long DefaultReadyAfterSeconds = 60;

    /// <summary>The longest wait a workflow can hold: two weeks, as much as the three waits it replaced could add up to.</summary>
    public const long MaxReadyAfterSeconds = 14 * 24 * 3600;

    /// <summary>What a new workflow's minimum file size is, in MB.</summary>
    public const long DefaultMinFileSizeMb = 50;

    /// <summary>The largest minimum or maximum file size a workflow can hold, in MB.</summary>
    public const long LargestSizeLimitMb = 1_000_000;

    /// <summary>
    /// The one wait that carries a workflow's three older waits forward: the wait after the file last changed plus the hold on
    /// every new file, or the wait for the size to stop growing when that is longer. Nothing is picked up sooner than before.
    /// </summary>
    /// <param name="ageSeconds">The wait after the file last changed, with Performance's value where the workflow had none.</param>
    /// <param name="holdMinutes">The hold on every new file, in minutes.</param>
    /// <param name="sizeStableSeconds">The wait for the size to stop growing; 0 when the workflow ignored size changes.</param>
    public static long ReadyAfterFromThreeWaits(long ageSeconds, long holdMinutes, long sizeStableSeconds) =>
        Math.Min(MaxReadyAfterSeconds, Math.Max(Math.Max(0, ageSeconds) + (Math.Max(0, holdMinutes) * 60), Math.Max(0, sizeStableSeconds)));
}
