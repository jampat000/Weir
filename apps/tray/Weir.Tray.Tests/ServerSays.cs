using System.Globalization;

namespace Weir.Tray.Tests;

/// <summary>Writes what the server writes into work-state.json, as of a chosen moment.</summary>
internal static class ServerSays
{
    internal static void Idle(string runtimeHome, DateTimeOffset checkedAt) => Write(runtimeHome, busy: false, checkedAt);

    internal static void Busy(string runtimeHome, DateTimeOffset checkedAt) => Write(runtimeHome, busy: true, checkedAt);

    internal static void Write(string runtimeHome, bool busy, DateTimeOffset checkedAt) =>
        File.WriteAllText(
            Path.Combine(runtimeHome, WorkStateFile.FileName),
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"busy":{{(busy ? "true" : "false")}},"checkedAt":"{{checkedAt.UtcDateTime:O}}"}"""));
}
