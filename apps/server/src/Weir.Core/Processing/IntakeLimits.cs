namespace Weir.Core.Processing;

/// <summary>
/// The smallest file a library takes and how long a file must have been left alone before Weir starts on it. A library
/// with no value of its own follows Settings › Performance, so changing that setting changes what such a library does (#815).
/// </summary>
/// <param name="MinFileSizeMb">Files smaller than this are skipped; 0 takes any size.</param>
/// <param name="MinFileAgeSeconds">A file must have gone this long without changing; 0 needs no wait.</param>
public readonly record struct IntakeLimits(long MinFileSizeMb, long MinFileAgeSeconds)
{
    /// <summary>The library's own values where it sets them, Performance's where it does not.</summary>
    /// <param name="library">The library the file belongs to, or null when the file has no library row.</param>
    /// <param name="performance">The Performance settings.</param>
    public static IntakeLimits Resolve(ProcessingLibraryRecord? library, ProcessingOperatorSettingsRecord performance)
    {
        ArgumentNullException.ThrowIfNull(performance);
        return new IntakeLimits(
            Math.Max(0, library?.MinFileSizeMb ?? performance.ProcessingMinInputFileSizeMb),
            Math.Max(0, library?.MinFileAgeSeconds ?? performance.MinFileAgeSeconds));
    }
}
