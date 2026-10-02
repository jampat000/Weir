using System.Numerics;
using Weir.Core.Json;
using Weir.Core.Processing;

namespace Weir.Infrastructure.Settings;

/// <summary>
/// Brings a backup written before a workflow's wait and minimum size lived on the workflow alone up to the current shape.
/// Such a workflow row has three waits and a minimum size that may be empty, meaning "use Setup › Performance › Speed"; the
/// backup's own Performance section says what that was.
/// </summary>
internal static class ConfigurationBundleIntakeUpgrade
{
    private const string ReadyAfter = "ready_after_seconds";
    private const string MinFileSize = "min_file_size_mb";
    private const long LegacyDefaultDetectionSeconds = 30;

    /// <summary>
    /// Give every workflow row that lacks <c>ready_after_seconds</c> the one wait its three waits add up to, and a minimum
    /// size where it had none of its own. Rows already in the current shape are left as they are.
    /// </summary>
    /// <param name="operatorSettings">The bundle's Performance section, which held the wait and size a workflow could follow.</param>
    /// <param name="libraryRows">The bundle's workflow rows.</param>
    public static void Apply(WireObject operatorSettings, IEnumerable<WireObject> libraryRows)
    {
        ArgumentNullException.ThrowIfNull(operatorSettings);
        ArgumentNullException.ThrowIfNull(libraryRows);
        var performanceAge = ReadNumber(operatorSettings, "min_file_age_seconds", LibraryIntake.DefaultReadyAfterSeconds);
        var performanceMinSize = ReadNumber(operatorSettings, "min_input_file_size_mb", LibraryIntake.DefaultMinFileSizeMb);
        foreach (var row in libraryRows)
        {
            if (row.ContainsKey(ReadyAfter))
            {
                continue;
            }

            var sizeWait = row.Get("ignore_size_changes") is { IsTruthy: true }
                ? 0
                : ReadNumber(row, "file_detection_interval_seconds", LegacyDefaultDetectionSeconds);
            row.Set(
                ReadyAfter,
                LibraryIntake.ReadyAfterFromThreeWaits(
                    ReadNumber(row, "min_file_age_seconds", performanceAge), ReadNumber(row, "hold_minutes", 0), sizeWait));
            row.Set(MinFileSize, Math.Max(0, ReadNumber(row, MinFileSize, performanceMinSize)));
        }
    }

    /// <summary>The whole number stored under <paramref name="key"/>, or <paramref name="fallback"/> when it is missing or empty.</summary>
    private static long ReadNumber(WireObject row, string key, long fallback) => row.Get(key) switch
    {
        WireInteger whole => (long)BigInteger.Clamp(whole.Value, long.MinValue, long.MaxValue),
        WireNumber { Value: var fraction } when double.IsFinite(fraction) => (long)Math.Clamp(fraction, long.MinValue, long.MaxValue),
        _ => fallback,
    };
}
