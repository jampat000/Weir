using System.Globalization;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// A write held back because its drive is short of the space a workflow keeps free: how it is worded and how far apart Weir
/// looks again. Shared by every write that can be held this way, so a file reads and retries the same wherever it waits.
/// </summary>
public static class DiskSpaceWaits
{
    /// <summary>
    /// Minutes between looks at a file waiting for room, the last one repeating. A full drive can stay full for days, so the
    /// looks spread out rather than fill Activity with one row every few minutes.
    /// </summary>
    public static readonly IReadOnlyList<int> LookMinutes = [10, 30, 60];

    /// <summary>How long to wait before the look after <paramref name="looksSoFar"/> looks.</summary>
    public static TimeSpan LookAfter(long looksSoFar) =>
        TimeSpan.FromMinutes(LookMinutes[(int)Math.Clamp(looksSoFar, 0, LookMinutes.Count - 1)]);

    /// <summary>The reason a file is on hold: which drive is short, by how much, and that Weir tries again.</summary>
    public static string Reason(string drive, double requiredMb, double freeMb) =>
        $"Waiting: the {drive} has less than {Gigabytes(requiredMb)} free ({Gigabytes(freeMb)} free now). Weir tries again when there is room.";

    public static string Gigabytes(double megabytes) => (megabytes / 1024).ToString("F1", CultureInfo.InvariantCulture) + " GB";
}
